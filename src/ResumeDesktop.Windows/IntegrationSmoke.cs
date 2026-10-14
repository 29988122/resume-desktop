using ResumeDesktop.Core;

namespace ResumeDesktop.Windows;

public static class IntegrationSmoke
{
    public static object Run(SnapshotStore store)
    {
        string sandbox = Path.Combine(Path.GetTempPath(), "ResumeDesktop-Smoke-" + Guid.NewGuid().ToString("N"));
        string a = Path.Combine(sandbox, "Example-A"), b = Path.Combine(sandbox, "Example-B");
        Directory.CreateDirectory(a); Directory.CreateDirectory(b);
        var baseline = Capture.Collect();
        var source = new Snapshot { Monitors = baseline.Monitors, Explorers =
        [
            Window([a, b, a], 1, 0), Window([a], 0, 1)
        ]};
        store.Save(source, true, "synthetic-smoke-source");
        var created = new List<RestoreEntry>();
        try
        {
            var first = Restore.Apply(source, store);
            created = store.LoadLedger(source.Id)?.Entries ?? [];
            Require(first.Completed.Count == 2 && first.Failures.Count == 0, "Initial restoration failed: " + string.Join("; ", first.Failures));
            Require(!first.Manual.Any(m => m.Contains("stacking order could not")), "Stacking check failed.");
            var native = Capture.Collect();
            Require(native.Explorers.Where(w => created.Any(e => e.Handle == w.Handle)).Sum(w => w.Tabs.Count) == 4, "Duplicate tabs were lost.");
            int count = native.Explorers.Count;
            var second = Restore.Apply(source, store);
            Require(second.Completed.Count == 2 && second.Failures.Count == 0 && Capture.Collect().Explorers.Count == count, "Repeat restoration created duplicates.");
            var actualSource = native with { Id = Guid.NewGuid().ToString("N"), Explorers = native.Explorers.Where(w => created.Any(e => e.Handle == w.Handle)).ToList() };
            store.Save(actualSource, true, "native-smoke-source");
            CloseOwned(created, sandbox);
            Require(created.All(e => !Native.SameLifetime(e.Handle, e.ProcessId, e.ProcessStarted)), "Test windows were not closed before native recovery.");
            var third = Restore.Apply(actualSource, store);
            created = store.LoadLedger(actualSource.Id)?.Entries ?? [];
            Require(third.Completed.Count == 2 && third.Failures.Count == 0, "Native snapshot recovery failed: " + string.Join("; ", third.Failures));
            Require(!third.Manual.Any(m => m.Contains("stacking order could not")), "Native stacking check failed.");
            var after = Capture.Collect();
            Require(baseline.Explorers.All(w => after.Explorers.Any(l => l.Handle == w.Handle && Matching.SameSession(w, l))), "Existing Explorer state changed.");
            return new { Passed = true, Windows = 2, Tabs = 4, DuplicatePath = true, RepeatedRestore = true, NativeSnapshotRecovery = true,
                ExistingWindowsPreserved = true, StackingVerified = true, RealExplorerCrashTested = false, Manual = first.Manual.Concat(third.Manual).Distinct().ToList() };
        }
        finally
        {
            CloseOwned(created, sandbox);
            CleanupSandbox(sandbox);
            if (Native.IsWindow((nint)baseline.ForegroundHandle)) Native.SetForegroundWindow((nint)baseline.ForegroundHandle);
        }
    }
    public static void CleanupSandbox(string sandbox)
    {
        var full = Path.GetFullPath(sandbox);
        if (!full.StartsWith(Path.Combine(Path.GetTempPath(), "ResumeDesktop-Smoke-"), StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(full)["ResumeDesktop-Smoke-".Length..], "N", out _))
            throw new ArgumentException("Not a test-owned sandbox.");
        using var shell = new ShellViews();
        foreach (var group in shell.Views.GroupBy(v => v.Frame))
        {
            if (group.Any(v => !v.Location.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
            if (Native.TabHandles(group.Key).Count != group.Count()) continue;
            CloseFrame(group.Key, full);
        }
    }
    private static ExplorerState Window(string[] paths, int active, int z) => new()
    {
        ZOrder = z, ActiveTab = active, TabOrderQuality = Quality.Verified,
        Placement = new(0, 1, new(0, 0), new(0, 0), new(120 + z * 60, 100 + z * 50, 960 + z * 60, 650 + z * 50)),
        Tabs = paths.Select((p, i) => new TabState { Name = Path.GetFileName(p), Location = p, LocationQuality = Quality.Verified, Order = i }).ToList()
    };
    private static void CloseOwned(List<RestoreEntry> entries, string sandbox)
    {
        foreach (var entry in entries)
        {
            if (!Native.SameLifetime(entry.Handle, entry.ProcessId, entry.ProcessStarted)) continue;
            var view = Capture.Collect().Explorers.FirstOrDefault(w => w.Handle == entry.Handle);
            if (view == null || view.Tabs.Count == 0 || view.Tabs.Any(t => !t.Location.StartsWith(sandbox + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
            CloseFrame((nint)entry.Handle, sandbox);
        }
    }
    private static void CloseFrame(nint frame, string sandbox)
    {
        for (int attempt = 0; attempt < 32 && Native.IsWindow(frame); attempt++)
        {
            using var shell = new ShellViews();
            var views = shell.Views.Where(v => v.Frame == frame).ToList();
            var tabs = Native.TabHandles(frame);
            if (views.Count == 0 || views.Count != tabs.Count || views.Any(v => !v.Location.StartsWith(sandbox + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) return;
            Native.PostMessage(frame, 0x0010, 0, 0);
            var limit = DateTime.UtcNow.AddSeconds(3);
            while (Native.IsWindow(frame) && Native.TabHandles(frame).Count >= tabs.Count && DateTime.UtcNow < limit) Thread.Sleep(50);
            if (Native.IsWindow(frame) && Native.TabHandles(frame).Count >= tabs.Count) return;
        }
    }
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
}
