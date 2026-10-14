using System.Runtime.InteropServices;
using System.Text;
using ResumeDesktop.Core;

namespace ResumeDesktop.Windows;

internal static class Native
{
    internal delegate bool WindowCallback(nint hwnd, nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct WINDOWPLACEMENT
    { public int Length, Flags, ShowCommand; public POINT Minimum, Maximum; public RECT Normal; }
    [DllImport("user32.dll")] internal static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint parent, WindowCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] internal static extern nint GetTopWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] internal static extern bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] internal static extern bool SetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint hwnd, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SendMessageTimeout(nint hwnd, uint message, nint wparam, nint lparam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] internal static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributes, out uint result);
    [DllImport("ole32.dll")] internal static extern void CoTaskMemFree(nint memory);
    [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    internal static string Class(nint hwnd) { var text = new StringBuilder(256); GetClassName(hwnd, text, text.Capacity); return text.ToString(); }
    internal static string Title(nint hwnd) { var text = new StringBuilder(1024); GetWindowText(hwnd, text, text.Capacity); return text.ToString(); }
    internal static List<nint> Frames()
    {
        return WindowsInZOrder().Where(h => Class(h) is "CabinetWClass" or "ExploreWClass").ToList();
    }
    internal static List<nint> WindowsInZOrder()
    {
        var result = new List<nint>();
        var visited = new HashSet<nint>();
        for (var h = GetTopWindow(0); h != 0 && visited.Add(h) && result.Count < 10000; h = GetWindow(h, 2))
            result.Add(h);
        return result;
    }
    internal static List<nint> TabHandles(nint frame)
    {
        var tabs = new List<nint>();
        EnumChildWindows(frame, (h, _) => { if (Class(h) == "ShellTabWindowClass") tabs.Add(h); return true; }, 0);
        return tabs;
    }
    internal static PlacementData? Placement(nint hwnd)
    {
        var p = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        return GetWindowPlacement(hwnd, ref p) ? new(p.Flags, p.ShowCommand, new(p.Minimum.X, p.Minimum.Y), new(p.Maximum.X, p.Maximum.Y),
            new(p.Normal.Left, p.Normal.Top, p.Normal.Right, p.Normal.Bottom)) : null;
    }
    internal static WINDOWPLACEMENT FromPlacement(PlacementData p) => new()
    {
        Length = Marshal.SizeOf<WINDOWPLACEMENT>(), Flags = p.Flags, ShowCommand = p.ShowCommand,
        Minimum = new() { X = p.Minimum.X, Y = p.Minimum.Y }, Maximum = new() { X = p.Maximum.X, Y = p.Maximum.Y },
        Normal = new() { Left = p.Normal.Left, Top = p.Normal.Top, Right = p.Normal.Right, Bottom = p.Normal.Bottom }
    };
    internal static long Started(int pid)
    {
        try { using var process = System.Diagnostics.Process.GetProcessById(pid); return process.StartTime.ToUniversalTime().Ticks; }
        catch (Exception e) when (e is ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException) { return 0; }
    }
    internal static bool SameLifetime(long hwnd, int pid, long started)
    {
        if (!IsWindow((nint)hwnd)) return false;
        GetWindowThreadProcessId((nint)hwnd, out var current);
        return current == pid && started != 0 && Started(pid) == started;
    }
}

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellServiceProvider
{
    [PreserveSig] int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object result);
}
[ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellBrowser
{
    [PreserveSig] int GetWindow(out nint hwnd);
    [PreserveSig] int ContextSensitiveHelp(bool enter);
    [PreserveSig] int InsertMenusSB(nint menu, nint widths);
    [PreserveSig] int SetMenuSB(nint menu, nint oleMenu, nint active);
    [PreserveSig] int RemoveMenusSB(nint menu);
    [PreserveSig] int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
    [PreserveSig] int EnableModelessSB(bool enable);
    [PreserveSig] int TranslateAcceleratorSB(nint message, ushort id);
    [PreserveSig] int BrowseObject(nint pidl, uint flags);
    [PreserveSig] int GetViewStateStream(uint mode, out nint stream);
    [PreserveSig] int GetControlWindow(uint id, out nint hwnd);
    [PreserveSig] int SendControlMsg(uint id, uint message, nint wparam, nint lparam, out nint result);
    [PreserveSig] int QueryActiveShellView(out nint view);
}
[ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManager
{
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint hwnd, [MarshalAs(UnmanagedType.Bool)] out bool current);
    [PreserveSig] int GetWindowDesktopId(nint hwnd, out Guid desktop);
    [PreserveSig] int MoveWindowToDesktop(nint hwnd, ref Guid desktop);
}
