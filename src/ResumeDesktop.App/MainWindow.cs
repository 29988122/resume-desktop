using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ResumeDesktop.Core;
using ResumeDesktop.Windows;
using Forms = System.Windows.Forms;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace ResumeDesktop.App;

internal sealed class MainWindow : Window
{
    private readonly SnapshotStore store;
    private readonly bool demo;
    private readonly ListBox history = new() { BorderThickness = new(0), Background = Brushes.Transparent };
    private readonly TextBlock state = new() { Foreground = Brush("#167D58"), FontSize = 13 };
    private readonly TextBlock summary = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 12) };
    private readonly ListBox windows = new() { BorderThickness = new(0) };
    private readonly ListBox apps = new() { BorderThickness = new(0) };
    private readonly ListBox taskbar = new() { BorderThickness = new(0) };
    private readonly WrapPanel images = new();
    private readonly TextBox report = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TabControl detailsTabs = new() { BorderThickness = new(0) };
    private readonly Forms.NotifyIcon tray;
    private readonly Recorder? recorder;
    private Snapshot? selected;
    private bool closing;
    private HotkeyRegistration? hotkey;
    internal MainWindow(SnapshotStore store, bool demo = false, bool hidden = false)
    {
        this.store = store; this.demo = demo;
        ShowActivated = !hidden;
        ShowInTaskbar = !hidden;
        Title = "Resume Desktop | 接續桌面";
        Width = 1080; Height = 740; MinWidth = 780; MinHeight = 520;
        Background = Brushes.White; Foreground = Brush("#202628"); FontFamily = new("Segoe UI"); FontSize = 13;
        var root = new Grid { Background = Brushes.White }; root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new());
        Content = root;
        var heading = new Grid { Margin = new(24, 20, 24, 16) }; heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var wordmark = new StackPanel(); wordmark.Children.Add(new TextBlock { Text = "Resume Desktop", FontSize = 26, FontWeight = FontWeights.SemiBold });
        wordmark.Children.Add(new TextBlock { Text = "接續桌面", Foreground = Brush("#65716D"), Margin = new(0, 4, 0, 0) });
        heading.Children.Add(wordmark); Grid.SetColumn(state, 1); state.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(state); root.Children.Add(heading);
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new(24, 0, 24, 16) };
        AddButton(toolbar, "\uE74E", "保存快照", async () => await Save(), "保存並保護目前工作階段");
        AddButton(toolbar, "\uE8A7", "預覽還原", async () => await Preview(), "比對目前視窗與選取的快照");
        AddButton(toolbar, "", "還原 Explorer", async () => await Apply(), "復用匹配視窗並補齊缺少的視窗與分頁");
        AddButton(toolbar, "", "重新整理", () => { Refresh(); return Task.CompletedTask; }, "重新讀取快照紀錄");
        var preferences = demo ? new Preferences(true) : store.LoadLocal<Preferences>("preferences.rdstate") ?? new(true);
        var screenshots = new WpfCheckBox { Content = "定期截圖", IsChecked = preferences.Screenshots, VerticalAlignment = VerticalAlignment.Center, Margin = new(20, 0, 12, 0) };
        screenshots.Checked += (_, _) => { if (recorder != null) recorder.VisualsEnabled = true; if (!demo) store.SaveLocal("preferences.rdstate", new Preferences(true)); };
        screenshots.Unchecked += (_, _) => { if (recorder != null) recorder.VisualsEnabled = false; if (!demo) store.SaveLocal("preferences.rdstate", new Preferences(false)); };
        toolbar.Children.Add(screenshots);
        var pause = new WpfCheckBox { Content = "暫停備份", VerticalAlignment = VerticalAlignment.Center };
        pause.Checked += (_, _) => { if (recorder != null) recorder.Paused = true; };
        pause.Unchecked += (_, _) => { if (recorder != null) recorder.Paused = false; };
        toolbar.Children.Add(pause); Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new(250) }); body.ColumnDefinitions.Add(new());
        var sidebar = new DockPanel { Background = Brush("#F3F6F4"), LastChildFill = true };
        var caption = new TextBlock { Text = "快照紀錄", FontWeight = FontWeights.SemiBold, Margin = new(18, 16, 18, 12) };
        DockPanel.SetDock(caption, Dock.Top); sidebar.Children.Add(caption); sidebar.Children.Add(history);
        body.Children.Add(sidebar);
        var panel = new DockPanel { Margin = new(22, 16, 22, 16) }; DockPanel.SetDock(summary, Dock.Top); panel.Children.Add(summary);
        var tabs = detailsTabs;
        tabs.Items.Add(new TabItem { Header = "Explorer", Content = windows });
        tabs.Items.Add(new TabItem { Header = "應用程式", Content = apps });
        tabs.Items.Add(new TabItem { Header = "工作列", Content = taskbar });
        tabs.Items.Add(new TabItem { Header = "畫面", Content = new ScrollViewer { Content = images, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = "驗證與診斷", Content = report });
        panel.Children.Add(tabs); Grid.SetColumn(panel, 1); body.Children.Add(panel); Grid.SetRow(body, 2); root.Children.Add(body);
        history.SelectionChanged += (_, _) => LoadSelection();
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Resume Desktop", Visible = !demo };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("開啟 Resume Desktop", null, (_, _) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add("保存保護快照", null, (_, _) => Dispatcher.Invoke(async () => await Save()));
        menu.Items.Add("還原最近事故快照", null, (_, _) => Dispatcher.Invoke(async () =>
        {
            var incident = store.List().FirstOrDefault(s => s.Reason == "before-window-loss");
            if (incident == null) { ShowWindow(); return; }
            selected = store.Load(incident.Id); await Apply(); ShowWindow();
        }));
        menu.Items.Add("登入時啟動", null, (_, _) => { try { Startup.Toggle(); } catch (Exception e) { Dispatcher.Invoke(() => report.Text = e.Message); } });
        menu.Items.Add("結束", null, (_, _) => Dispatcher.Invoke(() => { closing = true; Close(); }));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
        tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(ShowWindow);
        if (!demo)
        {
            recorder = new(store);
            recorder.VisualsEnabled = preferences.Screenshots;
            recorder.StatusChanged += message => Dispatcher.Invoke(() => { state.Text = message; Refresh(false); });
            recorder.Incident += info => Dispatcher.Invoke(() =>
            {
                tray.ShowBalloonTip(8000, "已保存視窗消失前快照", $"{info.Windows} 個視窗 / {info.Tabs} 個分頁。開啟後按「還原 Explorer」。", Forms.ToolTipIcon.Info);
            });
        }
        Closing += (_, e) => { if (!closing && !demo) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => { recorder?.Dispose(); tray.Dispose(); System.Windows.Application.Current.Shutdown(); };
        Refresh();
        SourceInitialized += (_, _) =>
        {
            if (demo) return;
            var source = (System.Windows.Interop.HwndSource)System.Windows.PresentationSource.FromVisual(this);
            hotkey = new(source.Handle);
            source.AddHook((nint hwnd, int message, nint wparam, nint lparam, ref bool handled) =>
            {
                if (message == 0x0312 && wparam == 77) { handled = true; _ = Save(); }
                return 0;
            });
        };
        Closed += (_, _) => hotkey?.Dispose();
        if (hidden) Loaded += (_, _) => Hide();
    }
    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private static void AddButton(Panel panel, string icon, string label, Func<Task> action, string tooltip)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = icon, FontFamily = new("Segoe MDL2 Assets"), Margin = new(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = label });
        var button = new WpfButton { Content = content, ToolTip = tooltip, Padding = new(10, 7, 10, 7), Margin = new(0, 0, 8, 0), Background = Brushes.White, BorderBrush = Brush("#D4DDD7") };
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } finally { button.IsEnabled = true; } };
        panel.Children.Add(button);
    }
    private void ShowWindow() { ShowInTaskbar = true; Show(); WindowState = WindowState.Normal; Activate(); }
    internal void ExportDemoImage(string path)
    {
        if (!demo) throw new InvalidOperationException("UI images may only be exported in synthetic demo mode.");
        var surface = (FrameworkElement)Content;
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path); encoder.Save(file);
        closing = true; Close();
    }
    private void Refresh(bool selectFirst = true)
    {
        if (demo)
        {
            selected = DemoSnapshot.Create();
            history.Items.Clear();
            history.Items.Add(new ListBoxItem { Content = "10/14 10:30:00\n2 視窗 · 4 分頁 · 保護", Padding = new(12, 10, 12, 10) });
            history.Items.Add(new ListBoxItem { Content = "10/14 10:29:45\n2 視窗 · 4 分頁", Padding = new(12, 10, 12, 10) });
            Display(); state.Text = "示範資料"; return;
        }
        var oldId = (history.SelectedItem as ListBoxItem)?.Tag as string;
        history.Items.Clear();
        foreach (var info in store.List().Take(120))
        {
            var item = new ListBoxItem { Tag = info.Id, Padding = new(12, 10, 12, 10), Content = $"{info.CapturedAt.ToLocalTime():MM/dd HH:mm:ss}\n{info.Windows} 視窗 · {info.Tabs} 分頁{(info.Protected ? " · 保護" : "")}{(!info.Complete ? " · 部分" : "")}" };
            history.Items.Add(item);
            if (info.Id == oldId) history.SelectedItem = item;
        }
        if (history.SelectedItem == null && (selectFirst || selected == null) && history.Items.Count > 0) history.SelectedIndex = 0;
    }
    private void LoadSelection()
    {
        if (history.SelectedItem is not ListBoxItem item || item.Tag is not string id) return;
        try { selected = store.Load(id); Display(); } catch (Exception e) { report.Text = e.Message; }
    }
    private void Display()
    {
        if (selected == null) return;
        summary.Text = $"{selected.CapturedAt.ToLocalTime():yyyy/MM/dd HH:mm:ss}   ·   {selected.Explorers.Count} 個視窗 / {selected.Explorers.Sum(w => w.Tabs.Count)} 個分頁";
        windows.Items.Clear(); apps.Items.Clear(); taskbar.Items.Clear(); images.Children.Clear();
        foreach (var window in selected.Explorers.OrderBy(w => w.ZOrder))
        {
            var text = $"#{window.ZOrder + 1}   {(window.Complete ? "已確認" : "部分資料")}   ·   桌面 {window.DesktopId[..Math.Min(8, window.DesktopId.Length)]}\n" +
                string.Join("\n", window.Tabs.Select((t, i) => $"    {(i == window.ActiveTab ? "●" : "○")} {i + 1}. {t.Location}")) +
                (window.Issues.Count > 0 ? "\n" + string.Join("\n", window.Issues) : "");
            windows.Items.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(4, 10, 4, 14) });
        }
        foreach (var app in selected.Applications) apps.Items.Add(new TextBlock { Text = $"{app.Title}\n{app.Executable}", TextWrapping = TextWrapping.Wrap, Margin = new(4, 8, 4, 8) });
        foreach (var item in selected.Taskbar) taskbar.Items.Add(new TextBlock { Text = $"{item.Order + 1}. {item.Label}", Margin = new(4, 7, 4, 7) });
        foreach (var visual in selected.Visuals)
        {
            var stack = new StackPanel { Width = 320, Margin = new(0, 8, 14, 16) };
            stack.Children.Add(new TextBlock { Text = $"{visual.Kind} · {visual.CapturedAt.ToLocalTime():HH:mm:ss}", Foreground = Brush("#65716D") });
            stack.Children.Add(new TextBlock { Text = visual.Label, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 4, 0, 5) });
            if (visual.Error == null)
                try
                {
                    var image = new BitmapImage();
                    using var stream = new MemoryStream(store.ReadAsset(visual.Id));
                    image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
                    stack.Children.Add(new System.Windows.Controls.Image { Source = image, MaxHeight = 220, Stretch = Stretch.Uniform });
                }
                catch (Exception) { stack.Children.Add(new TextBlock { Text = "畫面已到期或無法讀取。" }); }
            else stack.Children.Add(new TextBlock { Text = visual.Error, TextWrapping = TextWrapping.Wrap });
            images.Children.Add(stack);
        }
        report.Text = string.Join("\n", selected.Issues.Concat(selected.Explorers.SelectMany(w => w.Issues))) +
            "\n\n工作列排序及虛擬桌面分配：手動對照。\n檢視模式、排序及精確捲動位置：本預覽版尚未還原。";
    }
    private async Task Save()
    {
        if (demo) return;
        try { state.Text = "保存中…"; await Workers.Capture(store, true, true, true); Refresh(); }
        catch (Exception e) { report.Text = e.Message; }
    }
    private async Task Preview()
    {
        if (selected == null || demo) return;
        try
        {
            var output = await Workers.Run(["_preview", "--store", store.Root, "--snapshot", selected.Id], TimeSpan.FromSeconds(15));
            var plan = StateJson.Deserialize<RestorePlan>(System.Text.Encoding.UTF8.GetBytes(output));
            report.Text = $"復用：{plan.Items.Count(i => i.Action == "reuse")} 個視窗\n新增：{plan.Items.Count(i => i.Action == "create")} 個視窗\n無法直接還原：{plan.Items.Count(i => i.Action is "blocked" or "unavailable")} 個視窗\n保留額外現有視窗：{plan.ExtraWindows.Count} 個\n\n" +
                string.Join("\n\n", plan.Items.Select((item, i) => $"視窗 {i + 1} · {item.Action}\n" + string.Join("\n", item.Locations)));
            detailsTabs.SelectedIndex = 4;
        }
        catch (Exception e) { report.Text = e.Message; }
    }
    private async Task Apply()
    {
        if (selected == null || demo) return;
        if (recorder != null) recorder.Paused = true;
        try
        {
            state.Text = "還原中…";
            var output = await Workers.Run(["_restore", "--store", store.Root, "--snapshot", selected.Id], TimeSpan.FromMinutes(10));
            var result = StateJson.Deserialize<RestoreResult>(System.Text.Encoding.UTF8.GetBytes(output));
            report.Text = $"已還原：{result.Completed.Count} 個視窗\n未完成：{result.Failures.Count} 個視窗\n保留額外現有視窗：{result.ExtraWindows.Count} 個\n\n" +
                (result.Failures.Count > 0 ? "未完成原因：\n" + string.Join("\n", result.Failures) + "\n\n" : "") +
                "待手動處理與限制：\n" + string.Join("\n", result.Manual);
            detailsTabs.SelectedIndex = 4;
            state.Text = result.Failures.Count == 0 ? "還原已完成；請查看驗證結果" : $"部分還原；{result.Failures.Count} 項未完成";
        }
        catch (Exception e) { report.Text = e.Message; state.Text = "還原未完成"; }
        finally { if (recorder != null) recorder.Paused = false; }
    }
}

internal sealed record Preferences(bool Screenshots);

internal static class DemoSnapshot
{
    internal static Snapshot Create() => new()
    {
        CapturedAt = new DateTimeOffset(2026, 10, 14, 10, 30, 0, TimeSpan.FromHours(8)),
        Explorers =
        [
            new() { ZOrder = 0, ActiveTab = 1, TabOrderQuality = Quality.Verified, DesktopId = "workspace-1", Tabs =
            [
                new() { Location = @"C:\Example\Downloads", Order = 0, LocationQuality = Quality.Verified },
                new() { Location = @"C:\Example\Project", Order = 1, LocationQuality = Quality.Verified },
                new() { Location = @"C:\Example\Downloads", Order = 2, LocationQuality = Quality.Verified }
            ]},
            new() { ZOrder = 1, ActiveTab = 0, TabOrderQuality = Quality.Verified, DesktopId = "workspace-1", Tabs =
            [new() { Location = @"C:\Example\Downloads", Order = 0, LocationQuality = Quality.Verified }] }
        ],
        Applications = [new() { Title = "Project notes", Executable = @"C:\Example\Editor.exe" }, new() { Title = "Research", Executable = @"C:\Example\Browser.exe" }],
        Taskbar = [new("Explorer", "", new(0, 0, 40, 40), 0), new("Editor", "", new(40, 0, 80, 40), 1), new("Browser", "", new(80, 0, 120, 40), 2)]
    };
}
