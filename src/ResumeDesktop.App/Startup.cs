using Microsoft.Win32;

namespace ResumeDesktop.App;

internal static class Startup
{
    internal static void Toggle()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (key.GetValue("ResumeDesktop") != null) { key.DeleteValue("ResumeDesktop"); return; }
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Startup registration requires the published executable.");
        key.SetValue("ResumeDesktop", "\"" + executable + "\" --record");
    }
}
