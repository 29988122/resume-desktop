using System.Security.Cryptography;
using System.Text;

namespace ResumeDesktop.Core;

public static class Matching
{
    public static string Normalize(string location)
    {
        if (location.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(location, UriKind.Absolute, out var uri))
            location = uri.LocalPath;
        return location.Replace('/', '\\').TrimEnd('\\');
    }
    public static bool SameLocation(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    public static string Signature(ExplorerState window) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", window.Tabs.OrderBy(t => t.Order).Select(t => Normalize(t.Location).ToUpperInvariant())) + "\nactive:" + window.ActiveTab)));
    public static bool SameSession(ExplorerState a, ExplorerState b) =>
        a.Complete && b.Complete && a.Tabs.Count == b.Tabs.Count && a.ActiveTab == b.ActiveTab &&
        a.Tabs.OrderBy(t => t.Order).Zip(b.Tabs.OrderBy(t => t.Order)).All(pair => SameLocation(pair.First.Location, pair.Second.Location));

    public static RestorePlan Plan(Snapshot source, Snapshot live, RestoreLedger? ledger = null)
    {
        var available = live.Explorers.ToList();
        var items = new List<RestoreItem>();
        foreach (var window in source.Explorers.OrderBy(e => e.ZOrder))
        {
            if (!window.Complete)
            {
                items.Add(new(window.Id, null, "unavailable", window.Tabs.Select(t => t.Location).ToList()));
                continue;
            }
            var saved = ledger?.Entries.FirstOrDefault(e => e.SourceId == window.Id);
            var uncertain = available.FirstOrDefault(e => saved != null && e.Handle == saved.Handle && e.ProcessId == saved.ProcessId &&
                e.ProcessStarted == saved.ProcessStarted && !e.Complete);
            if (saved is { Handle: 0, Finished: false } || uncertain != null)
            {
                if (uncertain != null) available.Remove(uncertain);
                items.Add(new(window.Id, uncertain?.Handle, "blocked", window.Tabs.Select(t => t.Location).ToList()));
                continue;
            }
            var candidate = available.FirstOrDefault(e => e.Complete && saved != null && e.Handle == saved.Handle &&
                e.ProcessId == saved.ProcessId && e.ProcessStarted == saved.ProcessStarted &&
                (SameSession(window, e) || (!saved.Finished && Signature(e) == saved.Signature)));
            candidate ??= available.FirstOrDefault(e => e.Handle == window.Handle && e.ProcessId == window.ProcessId &&
                e.ProcessStarted == window.ProcessStarted && SameSession(window, e));
            candidate ??= available.Where(e => SameSession(window, e)).OrderBy(e => Distance(window, e)).FirstOrDefault();
            if (candidate != null) available.Remove(candidate);
            items.Add(new(window.Id, candidate?.Handle, candidate == null ? "create" : "reuse", window.Tabs.Select(t => t.Location).ToList()));
        }
        return new(items, available.Select(e => e.Handle).ToList());
    }
    private static long Distance(ExplorerState a, ExplorerState b) => a.Placement == null || b.Placement == null ? 0 :
        Math.Abs((long)a.Placement.Normal.Left - b.Placement.Normal.Left) + Math.Abs((long)a.Placement.Normal.Top - b.Placement.Normal.Top);
}
