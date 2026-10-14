using System.Runtime.InteropServices;

namespace ResumeDesktop.Windows;

public sealed class EventWatcher : IDisposable
{
    private delegate void Callback(nint hook, uint ev, nint hwnd, int objectId, int childId, uint thread, uint time);
    private readonly Callback callback;
    private readonly List<nint> hooks = [];
    public EventWatcher(Action changed)
    {
        callback = (_, _, hwnd, _, _, _, _) =>
        {
            if (hwnd != 0 && Native.Class(Native.GetAncestor(hwnd, 2)) is "CabinetWClass" or "ExploreWClass") changed();
        };
        foreach (var range in new[] { (0x0003u, 0x0003u), (0x8000u, 0x8003u), (0x800Bu, 0x800Cu) })
        {
            var hook = SetWinEventHook(range.Item1, range.Item2, 0, callback, 0, 0, 2);
            if (hook != 0) hooks.Add(hook);
        }
    }
    public void Dispose() { foreach (var hook in hooks) UnhookWinEvent(hook); GC.KeepAlive(callback); }
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, Callback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}
