using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;
using XIVChatStorage;

namespace XIVChat_Desktop;

// Opt-in entrypoint: uses only a unique fixture directory and fake notifications.
internal static class DesktopPrivacySmokeProgram {
    [STAThread] public static void Main() {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters => {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new DesktopPrivacySmokeApp();
        });
    }
}
internal sealed class DesktopPrivacySmokeApp : App {
    private readonly List<string> results = new();
    private readonly string output = Path.Combine(AppContext.BaseDirectory, "privacy-smoke-results.txt");
    private readonly string fixture = Path.Combine(AppContext.BaseDirectory, "privacy-fixture-" + Guid.NewGuid().ToString("N"));
    private void Check(bool value, string label) {
        if (!value) throw new Exception(label);
        results.Add("PASS " + label); File.WriteAllLines(output, results);
    }
    private static T Control<T>(Window window, string name) where T : FrameworkElement => (T)((FrameworkElement)window.Content).FindName(name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject {
        if (root is T value) yield return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static string InlineText(Inline inline) => inline is Run run ? run.Text : inline is Span span ? string.Concat(span.Inlines.Select(InlineText)) : "";
    private static string VisibleText(Window window) => string.Join("\n", Descendants<TextBlock>((DependencyObject)window.Content).Select(t => t.Text)
        .Concat(Descendants<RichTextBlock>((DependencyObject)window.Content).SelectMany(t => t.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.Select(InlineText)))));
    private static async Task Until(Func<bool> ready, string label) {
        for (int i = 0; i < 200; i++) { if (ready()) return; await Task.Delay(50); }
        throw new Exception("Timed out: " + label);
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private async Task CheckMessageBounds(Window window, string label) {
        var root = (FrameworkElement)window.Content;
        var view = Descendants<Controls.ChatMessageList>(root).Single();
        var list = Descendants<ListView>(view).Single();
        var origin = list.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        async Task<(byte[] Pixels, int Width, int Height)> Pixels() {
            var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(root);
            var buffer = await bitmap.GetPixelsAsync(); using var reader = DataReader.FromBuffer(buffer);
            var bytes = new byte[buffer.Length]; reader.ReadBytes(bytes);
            return (bytes, bitmap.PixelWidth, bitmap.PixelHeight);
        }
        var visible = await Pixels();
        list.Opacity = 0;
        var hidden = await Pixels();
        list.Opacity = 1;
        int outside = 0, inside = 0;
        double scaleX = visible.Width / root.ActualWidth, scaleY = visible.Height / root.ActualHeight;
        for (int y = 0; y < visible.Height; y++) for (int x = 0; x < visible.Width; x++) {
            int offset = (y * visible.Width + x) * 4;
            if (visible.Pixels[offset] == hidden.Pixels[offset] && visible.Pixels[offset + 1] == hidden.Pixels[offset + 1]
                && visible.Pixels[offset + 2] == hidden.Pixels[offset + 2]) continue;
            if (x < Math.Floor(origin.X * scaleX) || x >= Math.Ceiling((origin.X + list.ActualWidth) * scaleX)
                || y < Math.Floor(origin.Y * scaleY) || y >= Math.Ceiling((origin.Y + list.ActualHeight) * scaleY)) outside++;
            else inside++;
        }
        Check(inside > 100 && outside == 0, $"{label}: rendered chat stays inside viewport ({inside} visible, {outside} escaped pixels; {list.ActualWidth} x {list.ActualHeight})");
    }
    private static async Task Capture(Window window, string path) {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync((FrameworkElement)window.Content);
        var buffer = await bitmap.GetPixelsAsync(); using var reader = DataReader.FromBuffer(buffer);
        var bytes = new byte[buffer.Length]; reader.ReadBytes(bytes); File.WriteAllBytes(path, Array.Empty<byte>());
        var file = await StorageFile.GetFileFromPathAsync(path); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
    private sealed class Sink : INotificationSink {
        public readonly List<NotificationDelivery> Deliveries = new();
        public int Clears;
        public bool Show(NotificationDelivery delivery) { Deliveries.Add(delivery); return true; }
        public Task ClearAsync() { Clears++; Deliveries.Clear(); return Task.CompletedTask; }
        public void Dispose() { }
    }
    private void CheckOutboundPackets(CharacterIdentity owner, CharacterIdentity peer) {
        // Exercise the actual outbound queue without ever opening a transport.
        if (owner.Key == null) throw new Exception("Fixture owner has no identity key");
        var connection = new Connection(this, "127.0.0.1", 1);
        void Set(string name, object value) => typeof(Connection).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(connection, value);
        Set("available", true);
        Set("capabilities", new ServerCapabilities { GuardedCommands = true, DirectedTell = true });
        Set("commandPlayer", Session.Player!); Set("channelRevision", 17L);
        var queue = (System.Threading.Channels.Channel<byte[]>)typeof(Connection).GetField("outgoingMessages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!;
        XIVChatCommon.Message.Client.ClientMessage Read() {
            if (!queue.Reader.TryRead(out var packet)) throw new Exception("No outbound test packet");
            if (packet[0] != (byte)ClientOperation.Message) throw new Exception("Wrong outbound packet type");
            return XIVChatCommon.Message.Client.ClientMessage.Decode(packet.Skip(1).ToArray());
        }
        const string body = "中文发送测试 🐇\n第二行";
        var target = TellTarget.From(peer);
        var request = connection.SendTell(target, body, owner.Key, "fixture-login");
        var tell = Read();
        Check(request != null && tell.RequestId == request && tell.Content == body && tell.TellTarget?.Name == peer.Name
            && tell.ExpectedOwnerKey == owner.Key && tell.ExpectedOwnerEpoch == "fixture-login",
            "Tell packet preserves real recipient and multiline UTF-8 text while streamer mode is enabled");
        request = connection.SendMessageWithId(body);
        var channel = Read();
        Check(request != null && channel.RequestId == request && channel.Content == body && channel.TellTarget == null
            && channel.ExpectedChannelRevision == 17 && channel.ExpectedOwnerKey == owner.Key,
            "Channel packet carries the current owner and channel revision");
        Check(connection.SendTell(target, body, "different-owner", "fixture-login") == null
            && connection.SendTell(target, body, owner.Key, "old-login") == null,
            "Stale character and login identity cannot queue a tell");
        string oversized = new('中', 2731); // 8,193 UTF-8 bytes despite fewer characters.
        Check(connection.SendMessageWithId(oversized) == null && connection.SendTell(target, oversized, owner.Key, "fixture-login") == null
            && connection.SendMessageWithId(" \n ") == null, "Oversized UTF-8 and empty messages are rejected before queuing");
        Set("available", false);
        Check(connection.SendMessageWithId(body) == null && connection.SendTell(target, body, owner.Key, "fixture-login") == null,
            "Unavailable connection rejects channel and tell packets");
        Set("available", true); connection.Disconnect();
        Check(connection.SendMessageWithId(body) == null && !queue.Reader.TryRead(out _), "Cancelled connection queues no messages");
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args) {
        try {
            Directory.CreateDirectory(fixture);
            var config = new Configuration { FilePathOverride = Path.Combine(fixture, "config.json"), OnlineAvatars = false,
                Tabs = new ObservableCollection<Tab> { new("General") { Filter = Tab.GeneralFilter() } } };
            config.Privacy.Enabled = true; config.Privacy.SelfName = "Host";
            config.NotificationOptions.LoginLogout = true;
            typeof(App).GetProperty(nameof(Config))!.SetValue(this, config);
            LocalizationHelper.Initialize(AppLanguage.English);
            config.Save();
            var deserialize = typeof(Configuration).GetMethod("Deserialize", BindingFlags.Static | BindingFlags.NonPublic)!;
            var reloaded = (Configuration)deserialize.Invoke(null, new object[] { File.ReadAllText(config.FilePathOverride!) })!;
            Check(reloaded.Privacy.Enabled && reloaded.Privacy.Seed == config.Privacy.Seed && reloaded.Privacy.SelfName == "Host", "Saved privacy settings restore before the first window");
            var legacy = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(config.FilePathOverride!)); legacy.Remove("Privacy");
            var upgraded = (Configuration)deserialize.Invoke(null, new object[] { legacy.ToString() })!;
            Check(!upgraded.Privacy.Enabled && upgraded.Privacy.HideSelf && upgraded.Privacy.HideOthers, "Old configs receive safe opt-in defaults");
            Session.Store = await HistoryStore.OpenAsync(Path.Combine(fixture, "history.sqlite3"));
            await Presentation.InitializeAsync();
            var owner = new CharacterIdentity { ContentId = 7101, Name = "Alice Snow", HomeWorldId = 1, HomeWorld = "Alpha" };
            var peer = new CharacterIdentity { ContentId = 7102, Name = "Bob Birch", HomeWorldId = 2, HomeWorld = "Beta" };
            Session.Source = "privacy-fixture";
            Session.SetPlayer(new PlayerData("Alpha", "Alpha", "Test area", owner.Name) { Identity = owner, OwnerEpoch = "fixture-login" });
            Workbench.SetContext(Session.Source, owner);
            var context = Presentation.Context(Session.Source, owner.Key, owner);
            await Presentation.SetNicknameAsync(context, peer, "Team captain");
            var sink = new Sink(); Notifier.Sink = sink;
            var message = new ServerMessage(DateTime.UtcNow, ChatType.TellIncoming, Encoding.UTF8.GetBytes(peer.Name), Encoding.UTF8.GetBytes("Hello Alice Snow. Bob Birch @ Beta is ready."),
                new() { new TextChunk("[Tell] Bob "), new TextChunk("Birch: "), new TextChunk("Hello Alice Snow. Bob Birch @ Beta is ready.") }) {
                Owner = owner, TellPeer = peer, MessageId = "privacy-message", Sequence = 1, ServiceId = "fixture", RunId = "fixture"
            };
            await Session.RecordAsync(message, Session.Source, true); Session.Add(message);
            await Workbench.FlushAsync();
            var main = new MainWindow(); typeof(App).GetProperty(nameof(Window))!.SetValue(this, main); main.Activate();
            await Until(() => main.Content.XamlRoot != null, "main window");
            await Workspace.RestoreAsync();
            var model = Workbench.Open(peer)!; main.Navigate("conversations"); main.ShowConversation(model);
            await Until(() => Descendants<RichTextBlock>((DependencyObject)main.Content).Any(t => t.Blocks.Count > 0), "conversation text");
            await Task.Delay(200);
            var alias = Presentation.Identity(peer, context).Name;
            Check(Control<ToggleButton>(main, "StreamerButton").IsChecked == true, "Main window shows active streamer mode at startup");
            Check(Control<TextBlock>(main, "LoggedInAs").Text == "Host", "Own account uses custom alias");
            Check(Control<TextBlock>(main, "ChatTitle").Text == alias, "Conversation header overrides nickname with pseudonym");
            var text = VisibleText(main);
            Check(!text.Contains(owner.Name) && !text.Contains(peer.Name) && !text.Contains("Team captain"), "Rendered main window does not expose real names or private nicknames");
            Check(Descendants<PersonPicture>((DependencyObject)main.Content).All(p => p.ProfilePicture == null && p.Initials == "?"), "Protected avatars use neutral placeholders");
            var popout = main.PopoutCurrent()!;
            await Until(() => popout.Content.XamlRoot != null, "popout"); await Task.Delay(200);
            Check(popout.Title.StartsWith(alias) && !VisibleText(popout).Contains(peer.Name) && !VisibleText(popout).Contains(owner.Name), "Popout title, target and message text are masked");
            Check(VisibleText(popout).Contains(LocalizationHelper.GetString("Privacy.Active")), "Popout keeps a visible privacy indicator");
            var row = (await Session.Store.GetMessageAsync(message.LocalStorageId!))!;
            var history = new HistoryResult(row with { Note = "Ask Alice Snow and Bob Birch" }, "");
            Check(!history.Heading.Contains(owner.Name) && !history.Heading.Contains(peer.Name) && !history.Note.Contains(owner.Name), "History headings, message previews and notes are masked");
            var entry = new ServerGameEvent { EventId = "login", ServiceId = "fixture", RunId = "fixture", Owner = owner, OwnerEpoch = "fixture-login", Kind = GameEventKind.Login, Timestamp = DateTime.UtcNow };
            await Notifier.ReceiveEventAsync(Session.Source, entry, null);
            await Until(() => sink.Deliveries.Count > 0, "login notification");
            Check(sink.Deliveries.All(d => !d.Candidate.Title.Contains(owner.Name) && !d.Candidate.Text.Contains(owner.Name)), "Notification text uses the current privacy policy");
            var export = new Export(new HistoryQuery(Source: Session.Source, OwnerKey: owner.Key), peer.Name); export.Activate();
            await Until(() => Control<ListView>(export, "PreviewList").Items.Count > 0, "export preview");
            Check(Control<ListView>(export, "PreviewList").Items.Cast<string>().All(s => !s.Contains(owner.Name) && !s.Contains(peer.Name)), "Export preview is masked");
            File.WriteAllText(Path.Combine(fixture, "export.txt"), "old content");
            var file = await StorageFile.GetFileFromPathAsync(Path.Combine(fixture, "export.txt"));
            var projection = Presentation.Engine.Snapshot();
            await Export.WriteFileAsync(Session.Store, file, new(Source: Session.Source), false, displayLine: (r, timestamps) => projection.ExportLine(r.Source, r.Message, timestamps));
            Check(!File.ReadAllText(file.Path).Contains(owner.Name) && !File.ReadAllText(file.Path).Contains(peer.Name), "Saved export uses masked text");
            var previousExport = File.ReadAllText(file.Path);
            using (var cancelled = new CancellationTokenSource()) {
                try {
                    await Export.WriteFileAsync(Session.Store, file, new(Source: Session.Source), false, token: cancelled.Token,
                        displayLine: (r, timestamps) => { cancelled.Cancel(); return "must not replace the file"; });
                    throw new Exception("Cancelled export unexpectedly committed");
                } catch (OperationCanceledException) { }
            }
            Check(File.ReadAllText(file.Path) == previousExport, "Cancellation during export preserves the original destination file");
            var configWindow = new ConfigWindow(config); configWindow.Activate();
            Control<TabView>(configWindow, "ConfigTabs").SelectedItem = Control<TabViewItem>(configWindow, "TabPrivacy");
            await Task.Delay(150);
            Check(Control<ToggleSwitch>(configWindow, "PrivacyEnabled").IsOn, "Settings show the shared privacy state");
            LocalizationHelper.ApplyLanguage(AppLanguage.ChineseSimplified); await Task.Delay(200);
            Check(Control<ToggleButton>(main, "StreamerButton").Content?.ToString() == "主播模式已开启" && Control<CheckBox>(configWindow, "PrivacySelf").Content?.ToString() == "隐藏自己的角色信息", "Chinese resources update open privacy controls");
            await Capture(configWindow, Path.Combine(AppContext.BaseDirectory, "privacy-settings-zh.png"));
            configWindow.Close(); export.Close(); main.Activate(); await Task.Delay(150);
            await Capture(main, Path.Combine(AppContext.BaseDirectory, "privacy-main-zh.png"));
            config.Privacy.Enabled = false; config.Save(); await Task.Delay(200);
            Check(sink.Clears > 0, "Policy changes clear previously issued app notifications");
            Check(Control<TextBlock>(main, "ChatTitle").Text == "Team captain" && popout.Title.StartsWith("Team captain"), "Disabling privacy restores nicknames in already-open windows");
            await Presentation.SetNicknameAsync(context, peer, "Raid buddy"); await Task.Delay(100);
            Check(Control<TextBlock>(main, "ChatTitle").Text == "Raid buddy" && popout.Title.StartsWith("Raid buddy"), "Nickname changes refresh main and popout immediately");
            Check((await Session.Store.GetMessageAsync(message.LocalStorageId!))!.Message.ContentText == message.ContentText && model.Peer.Name == peer.Name, "Rendering never changes history or real send targets");
            var editor = (Task<string?>)typeof(MainWindow).GetMethod("EditTextAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, new object[] { peer.Name, owner.Name })!;
            await Task.Delay(150);
            config.Privacy.Enabled = true; config.Privacy.HideSelf = false; config.Save(); await Task.Delay(100);
            await Until(() => editor.IsCompleted, "identity editor closes on privacy change");
            Check(await editor == null, "Enabling streamer mode dismisses an already-open private note editor");
            Check(Control<TextBlock>(main, "LoggedInAs").Text == owner.Name && Control<TextBlock>(main, "ChatTitle").Text != peer.Name, "Hide-others-only keeps self visible");
            config.Privacy.HideSelf = true; config.Privacy.HideOthers = false; config.Save(); await Task.Delay(100);
            Check(Control<TextBlock>(main, "LoggedInAs").Text == "Host" && Control<TextBlock>(main, "ChatTitle").Text == "Raid buddy", "Hide-self-only keeps others’ nickname visible");
            config.Privacy.HideOthers = true; config.Save(); await Task.Delay(100);
            main.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 540)); await Task.Delay(200);
            var button = Control<ToggleButton>(main, "StreamerButton");
            var position = button.TransformToVisual((UIElement)main.Content).TransformPoint(new Windows.Foundation.Point());
            Check(position.X >= 0 && position.X + button.ActualWidth <= ((FrameworkElement)main.Content).ActualWidth + 1, "Streamer button remains reachable in a compact window");
            await Presentation.FlushAsync();
            Check(File.Exists(config.FilePathOverride) && File.ReadAllText(config.FilePathOverride!).Contains("\"Enabled\": true"), "Last privacy state is saved to the isolated config");
            popout.Close(); main.Navigate("channels");
            for (int i = 0; i < 120; i++) {
                main.AddSystemMessage($"连接或通信中断：The server returned status code '409' when status code '101' was expected. ({i})");
                main.AddSystemMessage("已断开");
            }
            await Task.Delay(400);
            var chat = Descendants<Controls.ChatMessageList>((DependencyObject)main.Content).Single();
            Check(!Control<TextBox>(main, "Composer").IsEnabled, "Disconnected composer stays disabled during repeated errors");
            await CheckMessageBounds(main, "Disconnected main window following latest");
            chat.ScrollToMessage(config.Tabs[0].Messages[30]); await Task.Delay(300);
            for (int i = 0; i < 20; i++) main.AddSystemMessage("连接或通信中断：The channel has been closed.\n已断开");
            await Task.Delay(300);
            Check(!chat.FollowingLatest, "Appending disconnect errors preserves history reading");
            await CheckMessageBounds(main, "Disconnected main window reading history");
            main.AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 480)); await Task.Delay(300);
            await CheckMessageBounds(main, "Resized disconnected main window");
            await Capture(main, Path.Combine(AppContext.BaseDirectory, "chat-disconnected-main-zh.png"));
            Invoke(Descendants<Button>(chat).Single(b => b.Name == "LatestButton")); await Task.Delay(300);
            var scroll = Descendants<ScrollViewer>(chat).First();
            Check(chat.FollowingLatest && scroll.ScrollableHeight - scroll.VerticalOffset <= 2, "Return to latest still reaches the newest disconnect error");
            var channelPopout = main.PopoutCurrent()!;
            await Until(() => channelPopout.Content.XamlRoot != null, "channel popout");
            for (int i = 0; i < 80; i++) {
                const string body = "离线历史测试消息\n多行正文不能越过消息列表进入输入框。";
                main.AddMessage(new ServerMessage(DateTime.UtcNow, ChatType.Say, Encoding.UTF8.GetBytes(owner.Name), Encoding.UTF8.GetBytes(body),
                    new() { new TextChunk(body) }) { Owner = owner });
            }
            await Until(() => channelPopout.LoadedMessages.Count >= 80, "popout multiline backlog");
            channelPopout.AppWindow.Resize(new Windows.Graphics.SizeInt32(600, 420)); await Task.Delay(300);
            await Capture(channelPopout, Path.Combine(AppContext.BaseDirectory, "chat-disconnected-popout-latest-zh.png"));
            await CheckMessageBounds(channelPopout, "Disconnected channel popout");
            channelPopout.MessageView.ScrollToMessage(channelPopout.LoadedMessages[20]); await Task.Delay(300);
            await CheckMessageBounds(channelPopout, "Disconnected channel popout reading multiline history");
            await Capture(channelPopout, Path.Combine(AppContext.BaseDirectory, "chat-disconnected-popout-zh.png"));
            string mainDraft = "主窗口草稿：中文与 emoji 🐇\n第二行保留。";
            string popoutDraft = "小窗草稿：断线时不能丢失\n第二行。";
            var mainComposer = Control<TextBox>(main, "Composer");
            mainComposer.Text = mainDraft;
            Check(mainComposer.Text.Replace("\r\n", "\n").Replace('\r', '\n') == mainDraft, "Main composer preserves Chinese, emoji and line breaks");
            mainDraft = mainComposer.Text; // WinUI normalizes editor line endings.
            typeof(MainWindow).GetMethod("Submit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
            Check(mainComposer.Text == mainDraft, "Disconnected main-window submit preserves multiline draft");
            Check(!Workbench.Send(model, "must stay local") && model.Peer.Name == peer.Name,
                "Disconnected tell is rejected and retains the real recipient");
            channelPopout.Composer.Text = popoutDraft;
            Check(channelPopout.Composer.Text.Replace("\r\n", "\n").Replace('\r', '\n') == popoutDraft, "Popout composer preserves multiline Chinese text");
            popoutDraft = channelPopout.Composer.Text;
            Check(!channelPopout.Submit() && channelPopout.Composer.Text == popoutDraft && channelPopout.State.PendingDraft == null,
                "Disconnected popout submit preserves draft without queuing a send");
            main.Navigate("conversations"); main.Navigate("channels");
            Check(mainComposer.Text == mainDraft, "Switching views preserves the channel draft");
            await Workspace.SaveAsync();
            var savedLayout = (await Session.Store.LoadLayoutAsync("current"))!;
            Check(savedLayout.MainDrafts.Values.Contains(mainDraft) && savedLayout.Windows.Any(w => w.Key == channelPopout.State.Key && w.Draft == popoutDraft),
                "Main and popout multiline drafts round-trip through the layout database");
            await Workspace.ClosePopoutAsync(channelPopout);
            var reopened = main.PopoutCurrent()!;
            await Until(() => reopened.Content.XamlRoot != null, "reopened popout");
            Check(reopened.Composer.Text == popoutDraft && !reopened.CanSend, "Reopening offline popout restores the unsent draft");
            reopened.State.PendingDraft = "older pending draft";
            Workspace.SendReply(reopened.State.Key, "Disconnected", "older pending draft", true);
            Check(reopened.Composer.Text == popoutDraft && reopened.State.FailedDraft == "older pending draft" && reopened.State.PendingDraft == null,
                "A failed pending send preserves both the newer draft and failed text");
            WorkspaceWindows.ApplyBounds(main, new(-50000, -50000, 900, 540));
            await Task.Delay(150);
            var workArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(main.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
            Check(main.AppWindow.Position.X >= workArea.X && main.AppWindow.Position.Y >= workArea.Y
                && main.AppWindow.Position.X + main.AppWindow.Size.Width <= workArea.X + workArea.Width
                && main.AppWindow.Position.Y + main.AppWindow.Size.Height <= workArea.Y + workArea.Height,
                "A saved off-screen main window is brought back inside an available display");
            CheckOutboundPackets(owner, peer);
            results.Add("All desktop privacy checks completed."); File.WriteAllLines(output, results);
            await Workspace.ShutdownAsync();
        } catch (Exception ex) {
            results.Add("FAIL " + ex); File.WriteAllLines(output, results); Environment.ExitCode = 1; Exit();
        }
    }
}
