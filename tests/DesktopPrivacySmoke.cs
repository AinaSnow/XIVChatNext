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
            results.Add("All desktop privacy checks completed."); File.WriteAllLines(output, results);
            await Workspace.ShutdownAsync();
        } catch (Exception ex) {
            results.Add("FAIL " + ex); File.WriteAllLines(output, results); Environment.ExitCode = 1; Exit();
        }
    }
}
