using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResumeDesktop.Core;

public enum Quality { Verified, LastKnown, Unavailable }
public sealed record PointData(int X, int Y);
public sealed record RectData(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
public sealed record PlacementData(int Flags, int ShowCommand, PointData Minimum, PointData Maximum, RectData Normal);
public sealed record MonitorData(string Device, RectData Bounds, RectData WorkArea);
public sealed record TabState
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public long Handle { get; init; }
    public string Name { get; init; } = "";
    public string Location { get; init; } = "";
    public int Order { get; init; }
    public Quality LocationQuality { get; init; } = Quality.Unavailable;
    public List<string> SelectedItems { get; init; } = [];
}
public sealed record ExplorerState
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public long Handle { get; init; }
    public int ProcessId { get; init; }
    public long ProcessStarted { get; init; }
    public int ZOrder { get; init; }
    public string DesktopId { get; init; } = "";
    public string Monitor { get; init; } = "";
    public bool Topmost { get; init; }
    public PlacementData? Placement { get; init; }
    public Quality TabOrderQuality { get; init; } = Quality.Unavailable;
    public int ActiveTab { get; init; } = -1;
    public List<TabState> Tabs { get; init; } = [];
    public List<string> Issues { get; init; } = [];
    [JsonIgnore] public bool Complete => Tabs.Count > 0 && ActiveTab >= 0 && ActiveTab < Tabs.Count &&
        TabOrderQuality == Quality.Verified && Tabs.All(t => t.LocationQuality == Quality.Verified && t.Location.Length > 0);
}
public sealed record AppWindow
{
    public long Handle { get; init; }
    public int ProcessId { get; init; }
    public long ProcessStarted { get; init; }
    public string Title { get; init; } = "";
    public string Executable { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string DesktopId { get; init; } = "";
    public int ZOrder { get; init; }
    public PlacementData? Placement { get; init; }
    public string? Thumbnail { get; init; }
    public DateTimeOffset? ThumbnailAt { get; init; }
}
public sealed record TaskbarItem(string Label, string AutomationId, RectData Bounds, int Order);
public sealed record VisualAsset(string Id, string Kind, string Label, DateTimeOffset CapturedAt, string? Error = null);
public sealed record Snapshot
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public string WindowsVersion { get; init; } = "";
    public string ExplorerVersion { get; init; } = "";
    public long ForegroundHandle { get; init; }
    public List<MonitorData> Monitors { get; init; } = [];
    public List<ExplorerState> Explorers { get; init; } = [];
    public List<AppWindow> Applications { get; init; } = [];
    public List<TaskbarItem> Taskbar { get; init; } = [];
    public List<VisualAsset> Visuals { get; init; } = [];
    public List<string> Issues { get; init; } = [];
    [JsonIgnore] public bool CoreComplete => Issues.Count == 0 && Explorers.All(e => e.Complete);
}
public sealed record SnapshotInfo(string Id, DateTimeOffset CapturedAt, int Windows, int Tabs, bool Complete, bool Protected, string Reason);
public sealed record RestoreItem(string SourceId, long? ExistingHandle, string Action, List<string> Locations);
public sealed record RestorePlan(List<RestoreItem> Items, List<long> ExtraWindows);
public sealed record PendingNavigation(long Handle, int Index);
public sealed record RestoreEntry(string SourceId, long Handle, int ProcessId, long ProcessStarted, string Signature, bool Finished)
{
    public PendingNavigation? Pending { get; init; }
}
public sealed record RestoreLedger(string SnapshotId, List<RestoreEntry> Entries);
public sealed record RestoreResult(string SnapshotId, List<string> Completed, List<string> Failures, List<long> ExtraWindows, List<string> Manual);

public static class StateJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
    public static T Deserialize<T>(byte[] value) => JsonSerializer.Deserialize<T>(value, Options) ?? throw new InvalidDataException("Empty JSON.");
}
