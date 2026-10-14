using System.Runtime.InteropServices;
using System.Windows.Automation;
using ResumeDesktop.Core;

namespace ResumeDesktop.Windows;

public static class Restore
{
    public static RestoreResult Apply(Snapshot source, SnapshotStore store)
    {
        var before = Capture.Collect();
        store.Pin(source.Id, "restore-source");
        store.Save(before, true, "before-restore");
        var oldLedger = store.LoadLedger(source.Id);
        var plan = Matching.Plan(source, before, oldLedger);
        var entries = oldLedger?.Entries.ToList() ?? [];
        var completed = new List<string>();
        var failures = new List<string>();
        var manual = new List<string> { "Taskbar ordering and virtual desktop assignment are manual.", "View mode, sorting and exact scroll position are not restored in this preview." };
        foreach (var item in plan.Items)
        {
            var desired = source.Explorers.Single(e => e.Id == item.SourceId);
            try
            {
                if (item.Action == "unavailable") throw new InvalidOperationException("Source tab identity/order is not verified; this window was not changed.");
                if (item.Action == "blocked") throw new InvalidOperationException("An earlier creation is unresolved. Review existing windows before explicitly resetting recovery progress.");
                if (item.ExistingHandle == null)
                {
                    ValidateLocation(desired.Tabs[0].Location);
                    entries.RemoveAll(e => e.SourceId == desired.Id);
                    entries.Add(new(desired.Id, 0, 0, 0, "opening", false));
                    store.SaveLedger(new(source.Id, entries));
                }
                long frame = item.ExistingHandle ?? OpenWindow(desired.Tabs[0].Location);
                var live = Read(frame);
                var pending = entries.FirstOrDefault(e => e.SourceId == desired.Id)?.Pending;
                if (pending != null)
                {
                    if (pending.Index < 0 || pending.Index >= live.Tabs.Count || live.Tabs[pending.Index].Handle != pending.Handle)
                        throw new InvalidOperationException("Pending navigation identity changed.");
                    using var shell = new ShellViews();
                    var view = shell.ByTab((nint)pending.Handle) ?? throw new InvalidOperationException("Pending tab is not registered.");
                    ShellViews.Navigate(view, desired.Tabs[pending.Index].Location);
                    Wait(() => Matching.SameLocation(Read(frame).Tabs[pending.Index].Location, desired.Tabs[pending.Index].Location), "Pending navigation was not confirmed.");
                    live = Read(frame); pending = null;
                }
                SaveProgress(false);
                CheckPrefix(desired, live);
                for (int i = live.Tabs.Count; i < desired.Tabs.Count; i++)
                {
                    var unchanged = Read(frame);
                    if (Matching.Signature(unchanged) != Matching.Signature(live))
                        throw new InvalidOperationException("Window changed during recovery; paused to preserve your changes.");
                    var oldTabs = Native.TabHandles((nint)frame);
                    var active = unchanged.Tabs.ElementAtOrDefault(unchanged.ActiveTab)?.Handle ?? 0;
                    if (active == 0 || !Native.SameLifetime(frame, live.ProcessId, live.ProcessStarted)) throw new InvalidOperationException("Target lifetime changed.");
                    if (!Native.PostMessage((nint)active, 0x0111, 0xA21B, 0)) throw new InvalidOperationException("Explorer rejected the new-tab request.");
                    nint created = 0;
                    Wait(() =>
                    {
                        var added = Native.TabHandles((nint)frame).Except(oldTabs).ToList();
                        if (added.Count > 1) throw new InvalidOperationException("Unexpected concurrent tab creation.");
                        created = added.SingleOrDefault(); return created != 0;
                    }, "New tab did not appear.");
                    Wait(() =>
                    {
                        using var shell = new ShellViews();
                        var view = shell.ByTab(created);
                        return view != null;
                    }, "New tab was not registered.");
                    Wait(() => Read(frame).Complete, "New tab identity/order is not yet readable.");
                    live = Read(frame);
                    pending = new(created, i);
                    SaveProgress(false);
                    using (var shell = new ShellViews())
                    {
                        var view = shell.ByTab(created) ?? throw new InvalidOperationException("New tab disappeared.");
                        ShellViews.Navigate(view, desired.Tabs[i].Location);
                    }
                    Wait(() =>
                    {
                        using var shell = new ShellViews();
                        return shell.ByTab(created) is { } view && Matching.SameLocation(view.Location, desired.Tabs[i].Location);
                    }, "Tab navigation was not confirmed.");
                    live = Read(frame);
                    pending = null;
                    CheckPrefix(desired, live);
                    SaveProgress(false);
                }
                SelectTab(frame, desired.ActiveTab);
                Wait(() => Read(frame).ActiveTab == desired.ActiveTab, "Active tab was not confirmed.");
                live = Read(frame);
                if (!Matching.SameSession(desired, live)) throw new InvalidOperationException("Restored tab sequence differs from the source.");
                RestoreSelection(desired, live, manual);
                RestorePlacement(desired, frame, source, before, manual);
                SaveProgress(true);
                completed.Add(desired.Id);

                void SaveProgress(bool finished)
                {
                    entries.RemoveAll(e => e.SourceId == desired.Id);
                    entries.Add(new RestoreEntry(desired.Id, frame, live.ProcessId, live.ProcessStarted, Matching.Signature(live), finished) { Pending = pending });
                    store.SaveLedger(new(source.Id, entries));
                }
            }
            catch (Exception e)
            { failures.Add(desired.Id + ": " + e.Message); }
        }
        Thread.Sleep(350);
        var sorted = source.Explorers.Where(w => completed.Contains(w.Id)).OrderByDescending(w => w.ZOrder);
        foreach (var original in sorted)
        {
            var entry = entries.Single(e => e.SourceId == original.Id);
            if (!Native.SameLifetime(entry.Handle, entry.ProcessId, entry.ProcessStarted)) continue;
            var anchor = source.Applications.Where(a => a.ZOrder < original.ZOrder && Native.SameLifetime(a.Handle, a.ProcessId, a.ProcessStarted) &&
                before.Applications.Any(b => b.Handle == a.Handle && b.DesktopId == before.Applications.FirstOrDefault(c => c.Handle == before.ForegroundHandle)?.DesktopId))
                .OrderByDescending(a => a.ZOrder).FirstOrDefault();
            var after = original.Topmost ? (nint)(-1) : anchor != null ? (nint)anchor.Handle : 0;
            if (!Native.SetWindowPos((nint)entry.Handle, after, 0, 0, 0, 0, 0x0213))
                manual.Add("Windows rejected a stacking operation.");
        }
        var managedHandles = entries.Where(e => completed.Contains(e.SourceId)).Select(e => e.Handle).ToHashSet();
        // Activating a reused target can undo the requested stacking order.
        if (!managedHandles.Contains(before.ForegroundHandle) && Native.IsWindow((nint)before.ForegroundHandle))
            Native.SetForegroundWindow((nint)before.ForegroundHandle);
        var final = Capture.Collect();
        foreach (var original in source.Explorers.Where(w => completed.Contains(w.Id)).ToList())
        {
            var entry = entries.Single(e => e.SourceId == original.Id);
            var actual = final.Explorers.FirstOrDefault(w => w.Handle == entry.Handle);
            if (actual == null || !Matching.SameSession(original, actual))
            { completed.Remove(original.Id); failures.Add(original.Id + ": Final readback differs."); }
        }
        var actualOrder = Native.Frames().Where(h => entries.Any(e => completed.Contains(e.SourceId) && e.Handle == h)).Select(h => (long)h).ToList();
        var expectedOrder = source.Explorers.Where(w => completed.Contains(w.Id)).OrderBy(w => w.ZOrder).Select(w => entries.Single(e => e.SourceId == w.Id).Handle).ToList();
        if (!actualOrder.SequenceEqual(expectedOrder))
        {
            var firstHandle = expectedOrder.FirstOrDefault();
            if (firstHandle != 0 && !Native.IsIconic((nint)firstHandle))
            {
                try { AutomationElement.FromHandle((nint)firstHandle).SetFocus(); }
                catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException) { }
            }
            foreach (var handle in expectedOrder.AsEnumerable().Reverse()) Native.SetWindowPos((nint)handle, 0, 0, 0, 0, 0, 0x0213);
            actualOrder = Native.Frames().Where(h => expectedOrder.Contains((long)h)).Select(h => (long)h).ToList();
            if (!actualOrder.SequenceEqual(expectedOrder)) manual.Add("Explorer stacking order could not be fully reproduced; observed target ranks: " + string.Join(",", actualOrder.Select(h => expectedOrder.IndexOf(h))));
            else manual.Add("Explorer relative stacking was restored; placement relative to other applications may differ.");
        }
        return new(source.Id, completed, failures, plan.ExtraWindows, manual.Distinct().ToList());
    }
    private static long OpenWindow(string location)
    {
        ValidateLocation(location);
        var old = Native.Frames();
        dynamic application = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        try
        {
            application.ShellExecute(location, "", "", "opennewwindow", 1);
        }
        finally { Marshal.ReleaseComObject(application); }
        long result = 0;
        Wait(() =>
        {
            using var shell = new ShellViews();
            var added = Native.Frames().Except(old).Where(h => shell.Views.Any(v => v.Frame == h && Matching.SameLocation(v.Location, location))).ToList();
            if (added.Count > 1) throw new InvalidOperationException("Ambiguous concurrent window creation.");
            result = added.SingleOrDefault(); return result != 0;
        }, "Explorer did not create a distinct new window.");
        return result;
    }
    private static void ValidateLocation(string location)
    {
        if (Native.SHParseDisplayName(location, 0, out var parsed, 0x20000000, out var attributes) < 0 || parsed == 0)
            throw new InvalidOperationException("Location is unavailable.");
        Native.CoTaskMemFree(parsed);
        if ((attributes & 0x20000000) == 0) throw new InvalidOperationException("Location is not a Shell folder; executable/file launch was refused.");
    }
    internal static ExplorerState Read(long frame) => Capture.Collect().Explorers.SingleOrDefault(e => e.Handle == frame) ??
        throw new InvalidOperationException("Target window disappeared.");
    private static void CheckPrefix(ExplorerState desired, ExplorerState live)
    {
        if (!live.Complete || live.Tabs.Count > desired.Tabs.Count || !live.Tabs.Zip(desired.Tabs).All(pair => Matching.SameLocation(pair.First.Location, pair.Second.Location)))
            throw new InvalidOperationException("Target tab sequence is incomplete or changed; recovery paused.");
    }
    internal static void SelectTab(long hwnd, int index)
    {
        var root = AutomationElement.FromHandle((nint)hwnd);
        var tabs = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
        if (tabs.Count == 0 && index == 0) return;
        if (index < 0 || index >= tabs.Count) throw new InvalidOperationException("Tab index unavailable.");
        if (tabs[index].TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern)) ((SelectionItemPattern)pattern).Select();
        else throw new InvalidOperationException("Tab selection is not supported by this Explorer version.");
    }
    private static void RestoreSelection(ExplorerState desired, ExplorerState live, List<string> manual)
    {
        using var shell = new ShellViews();
        for (int i = 0; i < desired.Tabs.Count; i++)
        {
            if (desired.Tabs[i].SelectedItems.Count == 0) continue;
            var view = shell.ByTab((nint)live.Tabs[i].Handle);
            if (view == null) continue;
            try
            {
                dynamic document = ((dynamic)view.Dispatch).Document;
                try
                {
                    for (int n = 0; n < desired.Tabs[i].SelectedItems.Count; n++)
                        document.SelectItem(desired.Tabs[i].SelectedItems[n], n == 0 ? 0x1D : 1);
                }
                finally { Marshal.ReleaseComObject(document); }
            }
            catch (Exception) { manual.Add("Some selected items could not be restored."); }
        }
    }
    private static void RestorePlacement(ExplorerState desired, long hwnd, Snapshot source, Snapshot live, List<string> manual)
    {
        if (desired.Placement == null) return;
        var p = desired.Placement;
        if (!source.Monitors.SequenceEqual(live.Monitors))
        {
            var monitor = live.Monitors.FirstOrDefault(m => m.Device == desired.Monitor) ?? live.Monitors.FirstOrDefault();
            if (monitor != null)
            {
                var r = p.Normal;
                int width = Math.Clamp(r.Width, 240, Math.Max(240, monitor.WorkArea.Width));
                int height = Math.Clamp(r.Height, 160, Math.Max(160, monitor.WorkArea.Height));
                int offsetX = monitor.WorkArea.Left - monitor.Bounds.Left, offsetY = monitor.WorkArea.Top - monitor.Bounds.Top;
                int left = Math.Clamp(r.Left, monitor.WorkArea.Left - offsetX, monitor.WorkArea.Right - offsetX - width);
                int top = Math.Clamp(r.Top, monitor.WorkArea.Top - offsetY, monitor.WorkArea.Bottom - offsetY - height);
                p = p with { Normal = new(left, top, left + width, top + height) };
            }
            manual.Add("Monitor topology changed; windows were clamped to a visible work area. DPI-equivalent layout is not guaranteed.");
        }
        var native = Native.FromPlacement(p);
        if (!Native.SetWindowPlacement((nint)hwnd, ref native)) manual.Add("Some window placement could not be restored.");
    }
    private static void Wait(Func<bool> condition, string error)
    {
        var end = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < end) { if (condition()) return; Thread.Sleep(100); }
        throw new TimeoutException(error);
    }
}
