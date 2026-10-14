using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using ResumeDesktop.Core;
using ResumeDesktop.Windows;

namespace ResumeDesktop.App;

internal sealed class Recorder : IDisposable
{
    private readonly SnapshotStore store;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Mutex mutex;
    private readonly bool ownsMutex;
    private readonly EventWatcher watcher;
    private bool busy;
    private DateTimeOffset next = DateTimeOffset.MinValue, visualAt = DateTimeOffset.MinValue, desktopAt = DateTimeOffset.MinValue, pruneAt = DateTimeOffset.MinValue;
    private SnapshotInfo? previous;
    private Snapshot? previousState;
    private DateTimeOffset incidentUntil = DateTimeOffset.MinValue, lastCapture = DateTimeOffset.MinValue;
    internal bool VisualsEnabled { get; set; } = true;
    internal bool Paused { get; set; }
    internal event Action<string>? StatusChanged;
    internal event Action<SnapshotInfo>? Incident;
    internal Recorder(SnapshotStore store)
    {
        this.store = store;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(store.Root.ToUpperInvariant())))[..20];
        mutex = new(false, "Local\\ResumeDesktop.Recorder." + key);
        try { ownsMutex = mutex.WaitOne(0); } catch (AbandonedMutexException) { ownsMutex = true; }
        watcher = new EventWatcher(() => next = DateTimeOffset.UtcNow.AddMilliseconds(750));
        if (!ownsMutex) { StatusChanged?.Invoke("Another recorder is active."); return; }
        previous = store.List().FirstOrDefault(s => s.Windows > 0);
        if (previous != null) previousState = store.Load(previous.Id);
        timer.Tick += Tick; timer.Start();
    }
    private async void Tick(object? sender, EventArgs e)
    {
        if (busy || Paused || (DateTimeOffset.UtcNow < next && DateTimeOffset.UtcNow < lastCapture.AddSeconds(5)) || !VisualCapture.SystemInformationAvailable()) return;
        try
        {
            Directory.CreateDirectory(store.Root);
            using var lease = new FileStream(Path.Combine(store.Root, "restore.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        }
        catch (IOException) { return; }
        busy = true;
        try
        {
            bool visual = VisualsEnabled && DateTimeOffset.UtcNow >= visualAt;
            bool desktop = visual && DateTimeOffset.UtcNow >= desktopAt;
            var info = await Workers.Capture(store, visual, desktop);
            var captured = store.Load(info.Id);
            if (visual) visualAt = DateTimeOffset.UtcNow.AddSeconds(15);
            if (desktop) desktopAt = DateTimeOffset.UtcNow.AddSeconds(60);
            bool lost = previousState?.Explorers.Any(old => !captured.Explorers.Any(live => live.Handle == old.Handle &&
                live.ProcessId == old.ProcessId && live.ProcessStarted == old.ProcessStarted)) == true;
            if (previous is { Windows: > 0 } last && lost && DateTimeOffset.UtcNow >= incidentUntil)
            {
                store.Pin(last.Id, "before-window-loss");
                incidentUntil = DateTimeOffset.UtcNow.AddSeconds(10);
                Incident?.Invoke(last);
            }
            if (info.Complete) { previous = info; previousState = captured; }
            if (DateTimeOffset.UtcNow >= pruneAt) { store.Prune(DateTimeOffset.UtcNow); pruneAt = DateTimeOffset.UtcNow.AddHours(1); }
            StatusChanged?.Invoke(info.Complete ? $"已保存 {info.Windows} 個視窗 / {info.Tabs} 個分頁" : "已保存部分狀態；請查看診斷");
        }
        catch (Exception ex) { StatusChanged?.Invoke(ex.Message); }
        finally { busy = false; lastCapture = DateTimeOffset.UtcNow; next = lastCapture.AddSeconds(5); }
    }
    public void Dispose()
    {
        timer.Stop(); watcher.Dispose();
        if (ownsMutex) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
