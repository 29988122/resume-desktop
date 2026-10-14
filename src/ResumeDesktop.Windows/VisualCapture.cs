using System.Drawing;
using System.Drawing.Imaging;
using ResumeDesktop.Core;
using Screen = System.Windows.Forms.Screen;

namespace ResumeDesktop.Windows;

public static class VisualCapture
{
    public static Snapshot AddScreenshots(Snapshot snapshot, SnapshotStore store, bool fullDesktop)
    {
        var visuals = new List<VisualAsset>();
        var applications = snapshot.Applications.ToList();
        var cached = new List<AppWindow>();
        foreach (var info in store.List().Where(s => s.Id != snapshot.Id).Take(20))
        {
            try { cached.AddRange(store.Load(info.Id).Applications.Where(a => a.Thumbnail != null)); }
            catch (Exception e) when (e is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException) { }
            if (cached.Count > 0) break;
        }
        if (!SystemInformationAvailable()) return snapshot with { Issues = snapshot.Issues.Concat(["Visual capture skipped: desktop is locked or unavailable."]).ToList() };
        foreach (var screen in Screen.AllScreens)
        {
            if (fullDesktop) CaptureRegion(screen.Bounds, "desktop", screen.DeviceName, store, visuals);
            var bounds = screen.Bounds;
            var work = screen.WorkingArea;
            var regions = new[] { new Rectangle(bounds.Left, bounds.Top, bounds.Width, work.Top - bounds.Top),
                new Rectangle(bounds.Left, work.Bottom, bounds.Width, bounds.Bottom - work.Bottom),
                new Rectangle(bounds.Left, work.Top, work.Left - bounds.Left, work.Height),
                new Rectangle(work.Right, work.Top, bounds.Right - work.Right, work.Height) };
            foreach (var region in regions.Where(r => r.Width > 0 && r.Height > 0)) CaptureRegion(region, "taskbar", screen.DeviceName, store, visuals);
        }
        int budget = 0;
        for (int i = 0; i < applications.Count && budget < 24; i++)
        {
            var app = applications[i];
            if (Native.IsIconic((nint)app.Handle)) { UseCached(i, "Minimized; no fresh thumbnail."); continue; }
            if (!Native.GetWindowRect((nint)app.Handle, out var rect)) continue;
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width < 1 || height < 1 || width > 8192 || height > 8192) continue;
            budget++;
            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            var hdc = graphics.GetHdc();
            bool success;
            try { success = Native.PrintWindow((nint)app.Handle, hdc, 2); } finally { graphics.ReleaseHdc(hdc); }
            if (!success) { UseCached(i, "Window did not provide an image."); continue; }
            if (bitmap.GetPixel(width / 2, height / 2).ToArgb() == bitmap.GetPixel(width / 4, height / 4).ToArgb() &&
                bitmap.GetPixel(width / 2, height / 2).R == 0 && bitmap.GetPixel(width / 2, height / 2).G == 0 && bitmap.GetPixel(width / 2, height / 2).B == 0)
            { UseCached(i, "Blank/black preview; fresh contents cannot be verified."); continue; }
            using var thumbnail = new Bitmap(bitmap, new Size(Math.Min(960, width), Math.Max(1, (int)((double)height * Math.Min(960, width) / width))));
            using var stream = new MemoryStream();
            thumbnail.Save(stream, ImageFormat.Png);
            string id = store.SaveAsset(stream.ToArray());
            var at = DateTimeOffset.UtcNow;
            visuals.Add(new(id, "window", app.Title, at));
            applications[i] = app with { Thumbnail = id, ThumbnailAt = at };
        }
        return snapshot with { Visuals = visuals, Applications = applications };

        void UseCached(int index, string error)
        {
            var app = applications[index];
            var previous = cached.FirstOrDefault(c => c.Handle == app.Handle && c.ProcessId == app.ProcessId && c.ProcessStarted == app.ProcessStarted &&
                c.ProcessStarted != 0 && c.Thumbnail != null && store.HasAsset(c.Thumbnail));
            if (previous?.Thumbnail is { } asset && previous.ThumbnailAt is { } time)
            {
                applications[index] = app with { Thumbnail = asset, ThumbnailAt = time };
                visuals.Add(new(asset, "window (last known)", app.Title, time));
            }
            else visuals.Add(new("", "window", app.Title, DateTimeOffset.UtcNow, error));
        }
    }
    private static void CaptureRegion(Rectangle bounds, string kind, string label, SnapshotStore store, List<VisualAsset> visuals)
    {
        try
        {
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
            visuals.Add(new(store.SaveAsset(stream.ToArray()), kind, label, DateTimeOffset.UtcNow));
        }
        catch (Exception e) { visuals.Add(new("", kind, label, DateTimeOffset.UtcNow, e.GetType().Name)); }
    }
    public static bool SystemInformationAvailable()
    {
        var desktop = OpenInputDesktop(0, false, 0x0100);
        if (desktop == 0) return false;
        CloseDesktop(desktop); return true;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
}
