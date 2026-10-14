using System.Runtime.InteropServices;
using ResumeDesktop.Core;

namespace ResumeDesktop.Windows;

internal sealed record ShellView(nint Frame, nint Tab, string Name, string Location, List<string> Selection, object Browser, object Dispatch);
internal sealed class ShellViews : IDisposable
{
    private readonly List<object> owned = [];
    internal readonly List<ShellView> Views = [];
    internal readonly List<string> Errors = [];
    internal ShellViews()
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            owned.Add(shell);
            dynamic collection = shell.Windows();
            owned.Add(collection);
            for (int i = 0; i < (int)collection.Count; i++)
            {
                object? dispatch = null;
                try
                {
                    dispatch = collection.Item(i);
                    if (dispatch == null) continue;
                    owned.Add(dispatch);
                    dynamic view = dispatch;
                    if (!string.Equals(Path.GetFileName((string)view.FullName), "explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    var frame = (nint)(long)view.HWND;
                    var service = (IShellServiceProvider)dispatch;
                    var guid = typeof(IShellBrowser).GUID;
                    if (service.QueryService(ref guid, ref guid, out object browser) < 0) continue;
                    owned.Add(browser);
                    var shellBrowser = (IShellBrowser)browser;
                    if (shellBrowser.GetWindow(out var tab) < 0) continue;
                    var root = Native.GetAncestor(tab, 2);
                    if (Native.Class(root) is "CabinetWClass" or "ExploreWClass") frame = root;
                    string location = "";
                    string name = "";
                    var selected = new List<string>();
                    dynamic document = view.Document;
                    owned.Add(document);
                    dynamic folder = document.Folder; owned.Add(folder);
                    dynamic self = folder.Self; owned.Add(self);
                    location = (string)self.Path;
                    name = (string)self.Name;
                    if (location.Length == 0) location = (string)view.LocationURL;
                    dynamic selection = document.SelectedItems(); owned.Add(selection);
                    for (int n = 0; n < Math.Min(200, (int)selection.Count); n++)
                    {
                        dynamic item = selection.Item(n); owned.Add(item);
                        selected.Add((string)item.Path);
                    }
                    Views.Add(new(frame, tab, name, location, selected, browser, dispatch));
                }
                catch (Exception e) when (e is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                { Errors.Add("Shell view read failed: " + e.GetType().Name); }
            }
        }
        catch (Exception e) { Errors.Add("Shell collection unavailable: " + e.GetType().Name); }
    }
    internal ShellView? ByTab(nint tab) => Views.FirstOrDefault(v => v.Tab == tab);
    internal static void Navigate(ShellView view, string location, bool newWindow = false)
    {
        if (Native.SHParseDisplayName(location, 0, out var pidl, 0x20000000, out var attributes) < 0 || pidl == 0)
            throw new InvalidOperationException("Shell location cannot be resolved.");
        try
        {
            if ((attributes & 0x20000000) == 0) throw new InvalidOperationException("Location is not a Shell folder.");
            var hr = ((IShellBrowser)view.Browser).BrowseObject(pidl, newWindow ? 0x0002u : 0x0001u);
            Marshal.ThrowExceptionForHR(hr);
        }
        finally { Native.CoTaskMemFree(pidl); }
    }
    public void Dispose()
    {
        // RCW identities can be shared by several views; release each acquired object once.
        foreach (var value in owned.Distinct(ReferenceEqualityComparer.Instance).Reverse())
            if (Marshal.IsComObject(value)) try { Marshal.ReleaseComObject(value); } catch (InvalidComObjectException) { }
    }
}
