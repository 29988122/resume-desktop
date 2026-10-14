namespace ResumeDesktop.Windows;

public sealed class HotkeyRegistration(nint hwnd) : IDisposable
{
    public bool Registered { get; } = Native.RegisterHotKey(hwnd, 77, 0x4003, 0x7B);
    public void Dispose() { if (Registered) Native.UnregisterHotKey(hwnd, 77); }
}
