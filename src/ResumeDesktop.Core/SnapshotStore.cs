namespace ResumeDesktop.Core;

public interface IDataProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

public sealed class SnapshotStore(string root, IDataProtector protector)
{
    public string Root { get; } = Path.GetFullPath(root);
    private string StateDirectory => Path.Combine(Root, "snapshots");
    private string AssetDirectory => Path.Combine(Root, "assets");
    private static string SafeId(string id) => id.Length == 32 && id.All(Uri.IsHexDigit) ? id : throw new ArgumentException("Invalid snapshot identifier.");
    public SnapshotInfo Save(Snapshot snapshot, bool protect = false, string reason = "")
    {
        if (snapshot.SchemaVersion != 1) throw new InvalidDataException("Unsupported snapshot version.");
        Directory.CreateDirectory(StateDirectory);
        var id = SafeId(snapshot.Id);
        var previousMeta = Path.Combine(StateDirectory, id + ".meta.json");
        if (File.Exists(previousMeta))
        {
            var previous = StateJson.Deserialize<SnapshotInfo>(File.ReadAllBytes(previousMeta));
            protect |= previous.Protected;
            if (previous.Protected && reason.Length == 0) reason = previous.Reason;
        }
        AtomicWrite(Path.Combine(StateDirectory, id + ".rdstate"), protector.Protect(StateJson.Serialize(snapshot)));
        var info = new SnapshotInfo(id, snapshot.CapturedAt, snapshot.Explorers.Count, snapshot.Explorers.Sum(e => e.Tabs.Count), snapshot.CoreComplete, protect, reason);
        AtomicWrite(Path.Combine(StateDirectory, id + ".meta.json"), StateJson.Serialize(info));
        return info;
    }
    public Snapshot Load(string id)
    {
        var snapshot = StateJson.Deserialize<Snapshot>(protector.Unprotect(File.ReadAllBytes(Path.Combine(StateDirectory, SafeId(id) + ".rdstate"))));
        if (snapshot.SchemaVersion != 1 || snapshot.Id != id) throw new InvalidDataException("Unsupported or mismatched snapshot.");
        return snapshot;
    }
    public List<SnapshotInfo> List()
    {
        if (!Directory.Exists(StateDirectory)) return [];
        var result = new List<SnapshotInfo>();
        foreach (var file in Directory.EnumerateFiles(StateDirectory, "*.meta.json"))
            try
            {
                var info = StateJson.Deserialize<SnapshotInfo>(File.ReadAllBytes(file));
                if (File.Exists(Path.Combine(StateDirectory, SafeId(info.Id) + ".rdstate"))) result.Add(info);
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or ArgumentException) { }
        return result.OrderByDescending(e => e.CapturedAt).ToList();
    }
    public void Pin(string id, string reason)
    {
        var file = Path.Combine(StateDirectory, SafeId(id) + ".meta.json");
        var info = StateJson.Deserialize<SnapshotInfo>(File.ReadAllBytes(file));
        AtomicWrite(file, StateJson.Serialize(info with { Protected = true, Reason = info.Protected && info.Reason.Length > 0 ? info.Reason : reason }));
    }
    public string SaveAsset(byte[] data)
    {
        Directory.CreateDirectory(AssetDirectory);
        var id = Guid.NewGuid().ToString("N");
        AtomicWrite(Path.Combine(AssetDirectory, id + ".rdasset"), protector.Protect(data));
        return id;
    }
    public byte[] ReadAsset(string id) => protector.Unprotect(File.ReadAllBytes(Path.Combine(AssetDirectory, SafeId(id) + ".rdasset")));
    public bool HasAsset(string id) => File.Exists(Path.Combine(AssetDirectory, SafeId(id) + ".rdasset"));
    public void SaveLedger(RestoreLedger ledger) => AtomicWrite(Path.Combine(Root, SafeId(ledger.SnapshotId) + ".ledger.rdstate"), protector.Protect(StateJson.Serialize(ledger)));
    public RestoreLedger? LoadLedger(string id)
    {
        var file = Path.Combine(Root, SafeId(id) + ".ledger.rdstate");
        return File.Exists(file) ? StateJson.Deserialize<RestoreLedger>(protector.Unprotect(File.ReadAllBytes(file))) : null;
    }
    public void SaveLocal<T>(string name, T data)
    {
        if (Path.GetFileName(name) != name) throw new ArgumentException("Invalid local filename.");
        AtomicWrite(Path.Combine(Root, name), protector.Protect(StateJson.Serialize(data)));
    }
    public T? LoadLocal<T>(string name)
    {
        if (Path.GetFileName(name) != name) throw new ArgumentException("Invalid local filename.");
        var file = Path.Combine(Root, name);
        return File.Exists(file) ? StateJson.Deserialize<T>(protector.Unprotect(File.ReadAllBytes(file))) : default;
    }
    public void Prune(DateTimeOffset now, long assetLimit = 2L * 1024 * 1024 * 1024)
    {
        Directory.CreateDirectory(Root);
        FileStream lease;
        try { lease = new FileStream(Path.Combine(Root, "capture.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return; }
        using var captureLease = lease;
        var all = List();
        var keep = all.Where(s => s.Protected || s.CapturedAt >= now.AddDays(-7)).Select(s => s.Id).ToHashSet();
        foreach (var candidate in new[] { all.FirstOrDefault(), all.FirstOrDefault(s => s.Complete), all.FirstOrDefault(s => s.Windows > 0) })
            if (candidate != null) keep.Add(candidate.Id);
        foreach (var info in all.Where(s => !keep.Contains(s.Id))) DeleteSnapshot(info.Id);
        var protectedAssets = new HashSet<string>();
        var referencedAssets = new HashSet<string>();
        foreach (var info in List())
            try
            {
                var refs = Load(info.Id).Visuals.Where(v => v.Error == null).Select(v => v.Id);
                referencedAssets.UnionWith(refs);
                if (info.Protected) protectedAssets.UnionWith(refs);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException) { return; }
        if (!Directory.Exists(AssetDirectory)) return;
        var assets = new DirectoryInfo(AssetDirectory).GetFiles("*.rdasset");
        foreach (var asset in assets.Where(a => !referencedAssets.Contains(Path.GetFileNameWithoutExtension(a.Name)))) asset.Delete();
        long total = new DirectoryInfo(AssetDirectory).GetFiles("*.rdasset").Sum(f => f.Length);
        foreach (var info in List().Where(s => !s.Protected).OrderBy(s => s.CapturedAt))
        {
            if (total <= assetLimit) break;
            var snap = Load(info.Id);
            foreach (var visual in snap.Visuals.Where(v => v.Error == null && !protectedAssets.Contains(v.Id)))
            {
                var file = new FileInfo(Path.Combine(AssetDirectory, SafeId(visual.Id) + ".rdasset"));
                if (file.Exists) { total -= file.Length; file.Delete(); }
            }
        }
    }
    private void DeleteSnapshot(string id)
    {
        File.Delete(Path.Combine(StateDirectory, SafeId(id) + ".meta.json"));
        File.Delete(Path.Combine(StateDirectory, SafeId(id) + ".rdstate"));
        File.Delete(Path.Combine(Root, SafeId(id) + ".ledger.rdstate"));
    }
    public static void AtomicWrite(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(data); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
