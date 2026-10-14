using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using ResumeDesktop.Core;
using Screen = System.Windows.Forms.Screen;

namespace ResumeDesktop.Windows;

public static class Capture
{
    public static Snapshot Collect()
    {
        var monitors = Screen.AllScreens.Select(s => new MonitorData(s.DeviceName, Rect(s.Bounds), Rect(s.WorkingArea))).ToList();
        var handles = Native.WindowsInZOrder();
        var explorers = new List<ExplorerState>();
        var applications = new List<AppWindow>();
        using var shell = new ShellViews();
        IVirtualDesktopManager? desktops = null;
        try { desktops = (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"))!)!; } catch (COMException) { }
        try
        {
            for (int z = 0; z < handles.Count; z++)
            {
                var hwnd = handles[z];
                var cls = Native.Class(hwnd);
                Native.GetWindowThreadProcessId(hwnd, out var process);
                string desktop = "";
                if (desktops != null && desktops.GetWindowDesktopId(hwnd, out var id) == 0) desktop = id.ToString();
                if (cls is "CabinetWClass" or "ExploreWClass")
                {
                    explorers.Add(ReadExplorer(hwnd, (int)process, z, desktop, shell));
                }
                if (!Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, 4) != 0 || Native.Title(hwnd).Length == 0) continue;
                string executable = "";
                try { using var p = Process.GetProcessById((int)process); executable = p.MainModule?.FileName ?? ""; } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { }
                applications.Add(new() { Handle = hwnd, ProcessId = (int)process, ProcessStarted = Native.Started((int)process), Title = Native.Title(hwnd), Executable = executable, ClassName = cls, DesktopId = desktop, ZOrder = z, Placement = Native.Placement(hwnd) });
            }
        }
        finally { if (desktops != null) Marshal.ReleaseComObject(desktops); }
        var taskbar = new List<TaskbarItem>();
        foreach (var bar in handles.Where(h => Native.Class(h) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"))
            try
            {
                var element = AutomationElement.FromHandle(bar);
                var buttons = element.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                var ordered = buttons.Cast<AutomationElement>().Where(e => !e.Current.IsOffscreen && !e.Current.BoundingRectangle.IsEmpty).OrderBy(e => e.Current.BoundingRectangle.Left).ToList();
                for (int i = 0; i < ordered.Count; i++)
                {
                    var b = ordered[i]; var r = b.Current.BoundingRectangle;
                    taskbar.Add(new(b.Current.Name, b.Current.AutomationId, new((int)r.Left, (int)r.Top, (int)r.Right, (int)r.Bottom), i));
                }
            }
            catch (Exception e) when (e is ElementNotAvailableException or COMException or InvalidOperationException) { }
        return new()
        {
            WindowsVersion = Environment.OSVersion.Version.ToString(),
            ExplorerVersion = FileVersionInfo.GetVersionInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")).FileVersion ?? "",
            ForegroundHandle = Native.GetForegroundWindow(), Monitors = monitors, Explorers = explorers, Applications = applications, Taskbar = taskbar,
            Issues = shell.Errors
        };
    }
    private static ExplorerState ReadExplorer(nint hwnd, int pid, int z, string desktop, ShellViews shell)
    {
        var views = shell.Views.Where(v => v.Frame == hwnd).ToList();
        var nativeTabs = Native.TabHandles(hwnd);
        var issues = new List<string>();
        var result = new List<TabState>();
        int active = -1;
        bool orderKnown = true;
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var tabItems = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Cast<AutomationElement>().ToList();
            if (nativeTabs.Count == 1 && views.Count == 1)
            {
                result.Add(Tab(views[0], 0)); active = 0;
            }
            else
            {
                // Classic Explorer ribbon tabs are not folder tabs.
                tabItems = tabItems.Where(item => views.Any(v => item.Current.Name.Equals(v.Name, StringComparison.OrdinalIgnoreCase) ||
                    item.Current.Name.StartsWith(v.Name + " ", StringComparison.OrdinalIgnoreCase) ||
                    item.Current.Name.Equals(v.Location, StringComparison.OrdinalIgnoreCase))).ToList();
                var remaining = views.ToList();
                for (int i = 0; i < tabItems.Count; i++)
                {
                    var item = tabItems[i];
                    var label = item.Current.Name;
                    bool selected = item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) && ((SelectionItemPattern)pattern).Current.IsSelected;
                    var candidates = remaining.Where(v => label.Equals(v.Name, StringComparison.OrdinalIgnoreCase) ||
                        label.StartsWith(v.Name + " ", StringComparison.OrdinalIgnoreCase) || label.Equals(v.Location, StringComparison.OrdinalIgnoreCase)).ToList();
                    ShellView? chosen = candidates.FirstOrDefault(v => (int)item.Current.NativeWindowHandle != 0 && v.Tab == (nint)item.Current.NativeWindowHandle);
                    if (chosen == null && candidates.Count > 0 && candidates.All(v => Matching.SameLocation(v.Location, candidates[0].Location)))
                        chosen = selected ? candidates.FirstOrDefault(v => Native.IsWindowVisible(v.Tab)) ?? candidates[0] : candidates[0];
                    if (chosen == null)
                    {
                        orderKnown = false; issues.Add("Tab identity could not be mapped unambiguously.");
                        result.Add(new() { Name = label, Order = i });
                    }
                    else { remaining.Remove(chosen); result.Add(Tab(chosen, i)); }
                    if (selected) active = i;
                }
                if (remaining.Count > 0 || result.Count != nativeTabs.Count)
                {
                    orderKnown = false; issues.Add("Native, Shell and accessibility tab counts differ.");
                    foreach (var view in remaining) result.Add(Tab(view, result.Count));
                }
                if (active < 0 && result.Count == 1) active = 0;
            }
        }
        catch (Exception e) when (e is COMException or ElementNotAvailableException or InvalidOperationException)
        { orderKnown = false; issues.Add("Accessibility read failed: " + e.GetType().Name); }
        if (result.Count == 0)
        {
            orderKnown = false;
            foreach (var view in views) result.Add(Tab(view, result.Count));
            issues.Add("No verified tab ordering available.");
        }
        if (active < 0) issues.Add("Active tab could not be verified.");
        return new()
        {
            Id = $"{pid:x8}{Native.Started(pid):x16}{hwnd.ToInt64():x8}",
            Handle = hwnd, ProcessId = pid, ProcessStarted = Native.Started(pid), ZOrder = z, DesktopId = desktop,
            Monitor = Screen.FromHandle(hwnd).DeviceName, Placement = Native.Placement(hwnd),
            Topmost = (Native.GetWindowLongPtr(hwnd, -20).ToInt64() & 8) != 0,
            Tabs = result, ActiveTab = active, TabOrderQuality = orderKnown ? Quality.Verified : Quality.Unavailable, Issues = issues
        };
    }
    private static TabState Tab(ShellView view, int order) => new()
    {
        Id = Guid.NewGuid().ToString("N"), Handle = view.Tab, Name = view.Name, Location = view.Location, Order = order,
        LocationQuality = string.IsNullOrEmpty(view.Location) ? Quality.Unavailable : Quality.Verified, SelectedItems = view.Selection
    };
    private static RectData Rect(System.Drawing.Rectangle rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
