using System.Diagnostics;
using System.Reflection;
using ResumeDesktop.Core;

namespace ResumeDesktop.App;

internal static class Workers
{
    internal static ProcessStartInfo Command(IEnumerable<string> args)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }
    internal static async Task<string> Run(IEnumerable<string> args, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var process = Process.Start(Command(args)) ?? throw new InvalidOperationException("Helper did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            // Only the exact helper process started above is terminated.
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync(CancellationToken.None);
            throw new TimeoutException("Helper timed out; Explorer and other applications were not terminated.");
        }
        var text = await output;
        var diagnostics = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(diagnostics.Length > 0 ? diagnostics.Trim() : "Helper failed.");
        return text;
    }
    internal static async Task<SnapshotInfo> Capture(SnapshotStore store, bool visuals, bool desktop, bool protect = false)
    {
        var id = Guid.NewGuid().ToString("N");
        try { await Run(["_collect", "--capture-id", id, "--store", store.Root, ..(visuals ? new[] { "--visuals" } : []), ..(desktop ? new[] { "--desktop" } : []), ..(protect ? new[] { "--protect" } : [])], TimeSpan.FromSeconds(12)); }
        catch (TimeoutException)
        {
            var committed = store.List().FirstOrDefault(i => i.Id == id);
            if (committed == null) throw;
            return committed with { Reason = "Visual helper timed out; structural snapshot committed." };
        }
        return store.List().Single(i => i.Id == id);
    }
}
