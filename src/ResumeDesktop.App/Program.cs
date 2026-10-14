using System.Text;
using ResumeDesktop.Core;
using ResumeDesktop.Windows;

namespace ResumeDesktop.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Contains("--version")) { Console.WriteLine("Resume Desktop 0.1.0"); return 0; }
            if (args.FirstOrDefault() == "probe")
            {
                var snapshot = Capture.Collect();
                Console.WriteLine(Encoding.UTF8.GetString(StateJson.Serialize(args.Contains("--private-details") ? (object)snapshot :
                    new { snapshot.WindowsVersion, snapshot.ExplorerVersion, snapshot.CoreComplete,
                        Windows = snapshot.Explorers.Select(w => new { w.Handle, Tabs = w.Tabs.Count, w.ActiveTab, w.TabOrderQuality, w.Complete, w.Issues }), snapshot.Issues })));
                return snapshot.CoreComplete ? 0 : 2;
            }
            string root = Value(args, "--store") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResumeDesktop");
            var store = new SnapshotStore(root, new UserDataProtector());
            string command = args.FirstOrDefault() ?? "";
            if (command == "_collect")
            {
                Directory.CreateDirectory(root);
                using var captureLease = Lease(Path.Combine(root, "capture.lock"), FileShare.None);
                using var recoveryLease = Lease(Path.Combine(root, "restore.lock"), FileShare.ReadWrite);
                var snapshot = Capture.Collect() with { Id = Value(args, "--capture-id") ?? Guid.NewGuid().ToString("N") };
                store.Save(snapshot, args.Contains("--protect"), args.Contains("--protect") ? "manual" : "");
                if (args.Contains("--visuals")) { snapshot = VisualCapture.AddScreenshots(snapshot, store, args.Contains("--desktop")); store.Save(snapshot, args.Contains("--protect"), args.Contains("--protect") ? "manual" : ""); }
                Print(new { snapshot.Id, snapshot.CoreComplete }); return 0;
            }
            if (command is "capture" or "status")
            {
                if (command == "capture") Print(Workers.Capture(store, !args.Contains("--no-visuals"), true, true).GetAwaiter().GetResult());
                else Print(new { Snapshots = store.List().Take(10), Storage = store.Root });
                return 0;
            }
            if (command is "_preview" or "_restore" or "restore")
            {
                string id = Value(args, "--snapshot") ?? throw new ArgumentException("--snapshot is required.");
                var source = store.Load(id);
                if (command == "_preview" || (command == "restore" && !args.Contains("--apply")))
                { Print(Matching.Plan(source, Capture.Collect(), store.LoadLedger(id))); return 0; }
                if (command == "restore")
                {
                    var output = Workers.Run(["_restore", "--store", root, "--snapshot", id, ..(args.Contains("--reset-progress") ? new[] { "--reset-progress" } : [])], TimeSpan.FromMinutes(10)).GetAwaiter().GetResult();
                    Console.WriteLine(output); return StateJson.Deserialize<RestoreResult>(Encoding.UTF8.GetBytes(output)).Failures.Count == 0 ? 0 : 2;
                }
                Directory.CreateDirectory(root);
                using var lease = Lease(Path.Combine(root, "restore.lock"), FileShare.None);
                if (args.Contains("--reset-progress")) store.SaveLedger(new(id, []));
                var result = Restore.Apply(source, store);
                store.SaveLocal("last-restore.rdstate", result);
                Print(result); return 0;
            }
            if (command == "smoke")
            { Print(IntegrationSmoke.Run(store)); return 0; }
            if (command == "cleanup-smoke")
            { IntegrationSmoke.CleanupSandbox(Value(args, "--sandbox") ?? throw new ArgumentException("--sandbox required.")); return 0; }
            if (args.Contains("--help"))
            {
                Console.WriteLine("Resume Desktop | probe [--private-details] | capture --once [--no-visuals] | status | restore --snapshot <id> --explorer --preview|--apply | smoke --store <isolated-folder> | --demo | --record");
                return 0;
            }
            if (command.Length > 0 && command is not "--demo" and not "--record") throw new ArgumentException("Unknown command. Use --help.");
            var application = new System.Windows.Application();
            var window = new MainWindow(store, args.Contains("--demo"), args.Contains("--record"));
            if (Value(args, "--width") is { } width) window.Width = int.Parse(width);
            if (Value(args, "--height") is { } height) window.Height = int.Parse(height);
            if (Value(args, "--screenshot") is { } image)
            {
                if (!args.Contains("--demo")) throw new ArgumentException("Screenshots for documentation require --demo.");
                window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => window.ExportDemoImage(image));
            }
            application.Run(window);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }
    private static string? Value(string[] args, string option) { int index = Array.IndexOf(args, option); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    private static void Print<T>(T value) => Console.WriteLine(Encoding.UTF8.GetString(StateJson.Serialize(value)));
    private static FileStream Lease(string path, FileShare sharing)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, sharing); }
            catch (IOException) when (DateTime.UtcNow < until) { Thread.Sleep(100); }
        }
    }
}
