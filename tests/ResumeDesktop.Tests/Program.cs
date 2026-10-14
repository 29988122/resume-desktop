using ResumeDesktop.Core;

var tests = new List<(string Name, Action Test)>();
ExplorerState Window(long handle, params string[] locations) => new()
{
    Handle = handle, ProcessId = 1, ProcessStarted = 100, ActiveTab = 0, TabOrderQuality = Quality.Verified,
    Tabs = locations.Select((p, i) => new TabState { Location = p, Order = i, LocationQuality = Quality.Verified }).ToList()
};
Snapshot Snap(params ExplorerState[] windows) => new() { Explorers = windows.ToList() };
void Check(bool ok) { if (!ok) throw new InvalidOperationException("Assertion failed."); }
void Test(string name, Action action) => tests.Add((name, action));
Test("Same path windows retain multiplicity", () => { var plan = Matching.Plan(Snap(Window(1, @"C:\A"), Window(2, @"C:\A")), Snap(Window(3, @"C:\A"))); Check(plan.Items.Count(i => i.Action == "reuse") == 1 && plan.Items.Count(i => i.Action == "create") == 1); });
Test("Same path tabs retain multiplicity", () => Check(!Matching.SameSession(Window(1, @"C:\A", @"C:\A"), Window(2, @"C:\A"))));
Test("Tab ordering matters", () => Check(!Matching.SameSession(Window(1, @"C:\A", @"C:\B"), Window(2, @"C:\B", @"C:\A"))));
Test("Active tab matters", () => Check(!Matching.SameSession(Window(1, @"C:\A", @"C:\B"), Window(2, @"C:\A", @"C:\B") with { ActiveTab = 1 })));
Test("Extra live windows are retained", () => { var plan = Matching.Plan(Snap(Window(1, @"C:\A")), Snap(Window(2, @"C:\A"), Window(3, @"C:\B"))); Check(plan.ExtraWindows.SequenceEqual([3L])); });
Test("Unverified order is not restored", () => Check(Matching.Plan(Snap(Window(1, @"C:\A") with { TabOrderQuality = Quality.Unavailable }), Snap()).Items[0].Action == "unavailable"));
Test("Unknown active tab is incomplete", () => Check(!(Window(1, @"C:\A") with { ActiveTab = -1 }).Complete));
Test("Empty location is incomplete", () => Check(!Window(1, "").Complete));
Test("File URI matches local path", () => Check(Matching.SameLocation("file:///C:/Example%20Folder", @"C:\Example Folder")));
Test("UNC locations preserved", () => Check(Matching.SameLocation(@"\\server\share\A", @"\\SERVER\share\A\")));
Test("Partial ledger supports exact retry", () =>
{
    var source = Window(1, @"C:\A", @"C:\B"); var live = Window(2, @"C:\A");
    var ledger = new RestoreLedger("s", [new(source.Id, 2, 1, 100, Matching.Signature(live), false)]);
    Check(Matching.Plan(Snap(source), Snap(live), ledger).Items[0].Action == "reuse");
});
Test("Modified partial window is preserved", () =>
{
    var source = Window(1, @"C:\A", @"C:\B"); var old = Window(2, @"C:\A"); var current = Window(2, @"C:\X");
    var ledger = new RestoreLedger("s", [new(source.Id, 2, 1, 100, Matching.Signature(old), false)]);
    var plan = Matching.Plan(Snap(source), Snap(current), ledger); Check(plan.Items[0].Action == "create" && plan.ExtraWindows.Count == 1);
});
Test("Reused handle does not reuse partial ledger", () =>
{
    var source = Window(1, @"C:\A", @"C:\B"); var current = Window(2, @"C:\A") with { ProcessStarted = 200 };
    var ledger = new RestoreLedger("s", [new(source.Id, 2, 1, 100, Matching.Signature(current), false)]);
    Check(Matching.Plan(Snap(source), Snap(current), ledger).Items[0].Action == "create");
});
Test("Repeat exact plan creates no new windows", () => Check(Matching.Plan(Snap(Window(1, @"C:\A"), Window(2, @"C:\A")), Snap(Window(3, @"C:\A"), Window(4, @"C:\A"))).Items.All(i => i.Action == "reuse")));
Test("Unresolved creation is blocked instead of duplicated", () => {
    var source = Window(1, @"C:\A");
    var ledger = new RestoreLedger("s", [new(source.Id, 0, 0, 0, "opening", false)]);
    Check(Matching.Plan(Snap(source), Snap(), ledger).Items[0].Action == "blocked");
});
Test("Serialization retains ordered duplicate tabs", () =>
{
    var value = StateJson.Deserialize<Snapshot>(StateJson.Serialize(Snap(Window(1, @"C:\A", @"C:\B", @"C:\A"))));
    Check(value.Explorers[0].Tabs.Count == 3 && value.Explorers[0].Tabs[2].Location == @"C:\A");
});
Test("Unsupported schema rejected", () => WithStore(store => { bool threw = false; try { store.Save(Snap() with { SchemaVersion = 99 }); } catch (InvalidDataException) { threw = true; } Check(threw); }));
Test("Protected data roundtrip", () => WithStore(store => { var snap = Snap(Window(1, @"C:\A")); store.Save(snap, true, "manual"); Check(store.Load(snap.Id).Explorers.Count == 1 && store.List()[0].Protected); }));
Test("Incomplete commit is invisible", () => WithStore(store => { var dir = Path.Combine(store.Root, "snapshots"); Directory.CreateDirectory(dir); SnapshotStore.AtomicWrite(Path.Combine(dir, Guid.NewGuid().ToString("N") + ".rdstate"), [1, 2]); Check(store.List().Count == 0); }));
Test("Visual update cannot unpin a checkpoint", () => WithStore(store => { var snap = Snap(); store.Save(snap, true, "incident"); store.Save(snap); Check(store.List()[0].Protected && store.List()[0].Reason == "incident"); }));
Test("Recovery pin preserves incident classification", () => WithStore(store => { var snap = Snap(); store.Save(snap, true, "before-window-loss"); store.Pin(snap.Id, "restore-source"); Check(store.List()[0].Reason == "before-window-loss"); }));
Test("Corrupt index does not hide valid snapshot", () => WithStore(store => { var snap = Snap(); store.Save(snap); SnapshotStore.AtomicWrite(Path.Combine(store.Root, "snapshots", "broken.meta.json"), [1, 2]); Check(store.List().Count == 1); }));
Test("Identifier cannot escape store", () => WithStore(store => { bool threw = false; try { store.Load("../escape"); } catch (ArgumentException) { threw = true; } Check(threw); }));
Test("Protected history survives pruning", () => WithStore(store =>
{
    var old = Snap(Window(1, @"C:\A")) with { CapturedAt = DateTimeOffset.UtcNow.AddDays(-20) }; store.Save(old, true, "incident"); store.Prune(DateTimeOffset.UtcNow); Check(store.Load(old.Id).Id == old.Id);
}));
Test("Last nonempty snapshot survives empty session", () => WithStore(store =>
{
    var old = Snap(Window(1, @"C:\A")) with { CapturedAt = DateTimeOffset.UtcNow.AddDays(-20) }; store.Save(old); store.Save(Snap()); store.Prune(DateTimeOffset.UtcNow); Check(store.List().Any(i => i.Id == old.Id));
}));
Test("Missing asset raises honest read error", () => WithStore(store =>
{ bool threw = false; try { store.ReadAsset(Guid.NewGuid().ToString("N")); } catch (IOException) { threw = true; } Check(threw); }));
Test("Atomic replacement has no partial files", () => WithStore(store =>
{ var file = Path.Combine(store.Root, "test.json"); SnapshotStore.AtomicWrite(file, [1]); SnapshotStore.AtomicWrite(file, [2]); Check(File.ReadAllBytes(file)[0] == 2 && !Directory.GetFiles(store.Root, "*.partial").Any()); }));
int failures = 0;
foreach (var (name, action) in tests)
    try { action(); Console.WriteLine("PASS " + name); } catch (Exception e) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + e.Message); }
Console.WriteLine($"{tests.Count - failures}/{tests.Count} checks passed.");
return failures == 0 ? 0 : 1;
static void WithStore(Action<SnapshotStore> test)
{
    string dir = Path.Combine(Path.GetTempPath(), "ResumeDesktop-CoreTests-" + Guid.NewGuid().ToString("N"));
    try { test(new(dir, new TestProtector())); } finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}
sealed class TestProtector : IDataProtector
{
    public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x55)).ToArray();
    public byte[] Unprotect(byte[] data) => Protect(data);
}
