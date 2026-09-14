using System;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;

namespace XIVChat_Desktop;

public sealed class SetupWizard : Window {
    private static SetupWizard? active;
    private readonly App app = (App)Application.Current;
    private readonly StackPanel body = new() { Spacing = 12 };
    private readonly TextBlock title = new() { FontSize = 23, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock hint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox language = new(), location = new(), relays = new() { DisplayMemberPath = "Name" };
    private readonly TextBox name = new(), host = new() { Text = "127.0.0.1" }, port = new() { Text = "14777" };
    private readonly CheckBox tell = new(), duty = new(), lost = new(), sound = new(), dnd = new(), quiet = new(), connect = new() { IsChecked = true };
    private readonly TimePicker quietStart = new(), quietEnd = new();
    private readonly Button back = new(), next = new(), skip = new(), test = new(), cancelTest = new(), pair = new(), notificationTest = new();
    private readonly NotificationOptions draft;
    private readonly string directId = Guid.NewGuid().ToString("N");
    private AppLanguage selectedLanguage;
    private int step;
    private bool rendering, testing, saved;
    private Connection? trial;
    private CancellationTokenSource? testCancellation;
    public static void Show() { if (active == null) active = new SetupWizard(); active.Activate(); }
    public SetupWizard() {
        draft = JsonConvert.DeserializeObject<NotificationOptions>(JsonConvert.SerializeObject(app.Config.NotificationOptions))!;
        selectedLanguage = app.Config.Language;
        name.Text = T("我的游戏", "My game");
        tell.IsChecked = draft.Tell; duty.IsChecked = draft.DutyReady; lost.IsChecked = draft.ConnectionLost;
        sound.IsChecked = draft.Sound; dnd.IsChecked = draft.DoNotDisturb; quiet.IsChecked = draft.QuietHours;
        quietStart.Time = draft.QuietStart; quietEnd.Time = draft.QuietEnd;
        var panel = ThemeHelper.CreateSurface();
        panel.Padding = new Thickness(24); panel.RowSpacing = 18;
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(title);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); panel.Children.Add(scroll);
        Grid.SetRow(status, 2); panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        buttons.Children.Add(skip); buttons.Children.Add(back); buttons.Children.Add(next); Grid.SetRow(buttons, 3); panel.Children.Add(buttons);
        Content = panel; ThemeHelper.InitializeWindow(this); AppWindow.Resize(new Windows.Graphics.SizeInt32(610, 710));
        language.SelectionChanged += (_, _) => { if (rendering || language.SelectedIndex < 0) return; selectedLanguage = (AppLanguage)language.SelectedIndex; LocalizationHelper.ApplyLanguage(selectedLanguage); };
        location.SelectionChanged += (_, _) => {
            if (rendering) return;
            CancelTrial(); status.Text = "";
            if (location.SelectedIndex == 1 && host.Text == "127.0.0.1") host.Text = "";
            if (location.SelectedIndex == 0) host.Text = "127.0.0.1";
            Render();
        };
        foreach (var box in new[] { name, host, port }) box.TextChanged += (_, _) => { if (!rendering) CancelTrial(); };
        relays.SelectionChanged += (_, _) => { if (!rendering) CancelTrial(); };
        back.Click += (_, _) => { SyncDraft(); step--; Render(); };
        next.Click += (_, _) => {
            try { if (step == 1) _ = Target(); SyncDraft(); if (step == 3) Save(); else { step++; status.Text = ""; Render(); } }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        skip.Click += (_, _) => {
            var old = app.Config.SetupDeferred;
            var oldInitialized = app.Config.SetupInitialized;
            try { app.Config.SetupDeferred = true; app.Config.SetupInitialized = true; app.Config.Save(); Close(); }
            catch (Exception ex) { app.Config.SetupDeferred = old; app.Config.SetupInitialized = oldInitialized; status.Text = ex.Message; }
        };
        test.Click += Test;
        cancelTest.Click += (_, _) => { CancelTrial(); status.Text = T("已取消连接测试。", "Connection test cancelled."); };
        pair.Click += (_, _) => { var dialog = new RelayPairDialog(); dialog.Closed += (_, _) => { if (step == 1) Render(); }; dialog.Activate(); };
        notificationTest.Click += (_, _) => { SyncDraft(); status.Text = app.Notifier.PreviewTest(draft); };
        Closed += (_, _) => { if (ReferenceEquals(active, this)) active = null; if (!saved) CancelTrial(); LocalizationHelper.ApplyLanguage(app.Config.Language); };
        Localize.BindWindow(this, Render);
    }
    private static string T(string zh, string en) => SetupText.T(zh, en);
    private void Render() {
        if (rendering) return;
        rendering = true;
        try {
            Title = T("初始设置", "Initial setup");
            title.Text = $"{step + 1} / 4 · " + (step switch { 0 => T("选择客户端语言", "Choose your language"), 1 => T("连接游戏", "Connect your game"), 2 => T("设置通知", "Choose notifications"), _ => T("准备开始", "Ready to begin") });
            skip.Content = T("稍后设置", "Set up later"); back.Content = T("上一步", "Back"); next.Content = step == 3 ? T("保存设置", "Save settings") : T("下一步", "Next");
            back.IsEnabled = step > 0 && !testing; next.IsEnabled = !testing;
            body.Children.Clear(); body.Children.Add(hint);
            if (step == 0) {
                hint.Text = T("设置只需几步，之后随时可以在设置中修改。语言切换立即预览，保存后生效。", "A few steps get you started. You can change everything later. Preview the language now and save it when finished.");
                language.ItemsSource = new[] { T("跟随系统", "Follow system"), "简体中文", "English" }; language.SelectedIndex = (int)selectedLanguage;
                body.Children.Add(language);
            } else if (step == 1) {
                hint.Text = T("先在游戏中加载插件。直连端口必须与插件一致；第一次连接需要在两端确认设备信任。", "Load the plugin in the game first. The direct connection port must match the plugin. The first connection requires trust approval on both devices.");
                var selected = Math.Max(0, location.SelectedIndex);
                location.ItemsSource = new[] { T("游戏在同一台电脑", "Game on this computer"), T("游戏在另一台电脑", "Game on another computer"), T("自建中继", "Self-hosted relay") }; location.SelectedIndex = selected;
                body.Children.Add(location);
                if (selected == 2) {
                    var id = (relays.SelectedItem as SavedServer)?.Id;
                    relays.ItemsSource = app.Config.Servers.Where(s => s.Relay != null).ToArray();
                    relays.SelectedItem = app.Config.Servers.FirstOrDefault(s => s.Id == id && s.Relay != null) ?? app.Config.Servers.LastOrDefault(s => s.Relay != null);
                    relays.Header = T("已配对的连接", "Paired connection"); pair.Content = T("粘贴中继邀请并配对", "Pair using a relay invitation");
                    body.Children.Add(relays); body.Children.Add(pair);
                } else {
                    name.Header = T("连接名称", "Connection name"); host.Header = T("游戏电脑 IP 或主机名", "Game computer IP or hostname"); port.Header = T("插件端口", "Plugin port"); host.IsEnabled = selected != 0;
                    body.Children.Add(name); body.Children.Add(host); body.Children.Add(port);
                }
                test.Content = T("测试连接", "Test connection"); cancelTest.Content = T("取消测试", "Cancel test");
                test.IsEnabled = !testing; cancelTest.IsEnabled = testing;
                body.Children.Add(test); body.Children.Add(cancelTest);
            } else if (step == 2) {
                hint.Text = T("提醒排本和未在前台阅读的悄悄话。上下线和换地图默认只记录，关键词规则可稍后设置。", "Get duty-ready and unread tell notifications. Login and territory events are recorded silently by default. Keyword rules can be configured later.");
                tell.Content = T("悄悄话提醒", "Tell notifications"); duty.Content = T("排本就绪提醒", "Duty-ready notifications"); lost.Content = T("异常断线提醒", "Unexpected disconnect notifications");
                sound.Content = T("播放声音", "Play a sound"); dnd.Content = T("免打扰", "Do not disturb"); quiet.Content = T("启用免打扰时段", "Enable quiet hours");
                quietStart.Header = T("开始时间", "Start"); quietEnd.Header = T("结束时间", "End"); notificationTest.Content = T("发送测试通知", "Send test notification");
                foreach (var control in new UIElement[] { tell, duty, lost, sound, dnd, quiet, quietStart, quietEnd, notificationTest }) body.Children.Add(control);
                var system = new Button { Content = T("打开 Windows 通知设置", "Open Windows notification settings") };
                system.Click += async (_, _) => { try { await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:notifications")); } catch (Exception ex) { status.Text = ex.Message; } };
                body.Children.Add(system);
            } else {
                var target = Target();
                hint.Text = T("请确认设置：", "Review your settings:") + "\n\n" + LocalizationHelper.GetLanguageName(selectedLanguage) + "\n" + target.Name + "\n" + target.Description +
                    "\n\n" + T("排本 / 悄悄话 / 断线：", "Duty / tell / disconnect: ") + Flag(draft.DutyReady) + " / " + Flag(draft.Tell) + " / " + Flag(draft.ConnectionLost) +
                    "\n" + T("声音 / 免打扰 / 定时免打扰：", "Sound / DND / quiet hours: ") + Flag(draft.Sound) + " / " + Flag(draft.DoNotDisturb) + " / " + Flag(draft.QuietHours) +
                    "\n\n" + (trial?.SessionReady == true ? T("连接测试已通过。", "Connection test passed.") : T("尚未通过连接测试，也可以保存后再连接。", "Connection has not been verified; you can save and connect later.")) +
                    "\n\n" + T("默认将聊天历史在本地保留 90 天。历史保留和在线头像选项可在设置中修改。", "Chat history is kept locally for 90 days by default. History retention and online avatars can be changed in Settings.");
                connect.Content = T("保存后连接／保留已测试连接", "Connect after saving / keep the tested connection"); body.Children.Add(connect);
            }
        } finally { rendering = false; }
    }
    private static string Flag(bool value) => value ? T("开", "on") : T("关", "off");
    private void SyncDraft() {
        draft.Tell = tell.IsChecked == true; draft.DutyReady = duty.IsChecked == true; draft.ConnectionLost = lost.IsChecked == true;
        draft.Sound = sound.IsChecked == true; draft.DoNotDisturb = dnd.IsChecked == true; draft.QuietHours = quiet.IsChecked == true;
        draft.QuietStart = quietStart.Time; draft.QuietEnd = quietEnd.Time;
    }
    private SavedServer Target() {
        if (location.SelectedIndex == 2) return (relays.SelectedItem as SavedServer)?.Snapshot() ?? throw new ArgumentException(T("请先配对一个中继连接。", "Pair a relay connection first."));
        if (string.IsNullOrWhiteSpace(name.Text) || Uri.CheckHostName(host.Text.Trim()) == UriHostNameType.Unknown || !ushort.TryParse(port.Text, out var number) || number == 0)
            throw new ArgumentException(T("请输入名称、有效的 IP／主机名，以及 1–65535 的端口。", "Enter a name, valid IP / hostname and a port between 1 and 65535."));
        return new SavedServer(name.Text.Trim(), host.Text.Trim(), number) { Id = directId };
    }
    private void CancelTrial() {
        testCancellation?.Cancel();
        if (trial != null && ReferenceEquals(app.Connection, trial)) app.Disconnect();
        trial = null;
    }
    private async void Test(object sender, RoutedEventArgs args) {
        if (testing) return;
        try {
            var target = Target();
            if (app.Connection != null && !ReferenceEquals(app.Connection, trial)) throw new InvalidOperationException(T("已有活动连接。请先在主窗口断开，再测试其他连接。", "There is an active connection. Disconnect it in the main window before testing another."));
            CancelTrial(); testing = true; Render();
            status.Text = T("正在连接；出现设备信任提示时，请核对并在两端确认。", "Connecting; compare and approve device trust on both ends when prompted.");
            testCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            app.Connect(target, remember: false); trial = app.Connection;
            if (trial == null || !await trial.ReadyTask.WaitAsync(testCancellation.Token)) throw new InvalidOperationException(trial?.FailureMessage ?? T("连接失败。请检查地址、端口或中继授权，以及插件是否运行。", "Connection failed. Check the address, port or relay authorization and that the plugin is running."));
            status.Text = T("已连接到插件。若角色尚未登录，聊天发送会保持禁用。", "Connected to the plugin. Sending stays disabled until the game character is logged in.");
        } catch (Exception ex) { CancelTrial(); status.Text = ex is OperationCanceledException ? T("连接测试已取消或超时。", "Connection test cancelled or timed out.") : ex.Message; }
        finally { testing = false; testCancellation?.Dispose(); testCancellation = null; Render(); }
    }
    private void Save() {
        var target = Target(); var oldLanguage = app.Config.Language; var oldNotifications = app.Config.NotificationOptions;
        var oldVersion = app.Config.SetupVersion; var oldDeferred = app.Config.SetupDeferred; var oldLast = app.Config.LastSuccessfulConnection;
        var oldInitialized = app.Config.SetupInitialized;
        var added = !app.Config.Servers.Any(s => s.Id == target.Id);
        try {
            app.Config.Language = selectedLanguage; app.Config.NotificationOptions = draft; app.Config.SetupVersion = 1; app.Config.SetupDeferred = false; app.Config.SetupInitialized = true;
            if (added) app.Config.Servers.Add(target);
            if (trial?.SessionReady == true && ReferenceEquals(app.Connection, trial)) app.Config.LastSuccessfulConnection = target.Snapshot();
            app.Config.Save();
        } catch {
            app.Config.Language = oldLanguage; app.Config.NotificationOptions = oldNotifications; app.Config.SetupVersion = oldVersion; app.Config.SetupDeferred = oldDeferred; app.Config.SetupInitialized = oldInitialized; app.Config.LastSuccessfulConnection = oldLast;
            if (added) app.Config.Servers.Remove(target); throw;
        }
        saved = true;
        if (connect.IsChecked != true) CancelTrial();
        else if (app.Connection == null) app.Connect(target);
        else if (ReferenceEquals(app.Connection, trial)) trial!.RememberTarget = true;
        Close();
    }
}
