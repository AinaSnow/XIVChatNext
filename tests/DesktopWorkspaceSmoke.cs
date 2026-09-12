using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Sodium;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;
using XIVChatStorage;

namespace XIVChat_Desktop;

internal static class DesktopWorkspaceSmokeProgram {
    [STAThread] public static void Main() {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters => {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new DesktopWorkspaceSmokeApp();
        });
    }
}
internal sealed class DesktopWorkspaceSmokeApp : App {
    private readonly List<string> results = new();
    private readonly string output = Path.Combine(AppContext.BaseDirectory, "workspace-smoke-results.txt");
    private void Check(bool ok, string message) { if (!ok) throw new Exception(message); results.Add("PASS " + message); File.WriteAllLines(output, results); }
    private static async Task Until(Func<bool> predicate, string label, int limit = 15000) {
        for (int i = 0; i < limit; i += 50) { if (predicate()) return; await Task.Delay(50); }
        throw new Exception("Timed out: " + label);
    }
    private static T Control<T>(Window window, string name) where T : FrameworkElement => (T)((FrameworkElement)window.Content).FindName(name);
    private static async Task Capture(Window window, string name) {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync((FrameworkElement)window.Content);
        var buffer = await bitmap.GetPixelsAsync(); using var reader = DataReader.FromBuffer(buffer); var pixels = new byte[buffer.Length]; reader.ReadBytes(pixels);
        var path = Path.Combine(AppContext.BaseDirectory, name); File.WriteAllBytes(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels); await encoder.FlushAsync();
    }
    private sealed class Sink : INotificationSink { public bool Show(NotificationDelivery delivery) => true; public void Dispose() { } }
    private sealed class SyncProgress(Action<long> action) : IProgress<long> { public void Report(long value) => action(value); }

    protected override async void OnLaunched(LaunchActivatedEventArgs args) {
        try {
            var tab = new Tab("Social / 社交") { Filter = Tab.GeneralFilter() };
            typeof(App).GetProperty(nameof(Config))!.SetValue(this, new Configuration {
                OnlineAvatars = false, HistoryEnabled = true, LocalBacklogMessages = 10000, BacklogMessages = 0,
                Tabs = new ObservableCollection<Tab> { tab },
            });
            LocalizationHelper.Initialize(AppLanguage.English); Notifier.Sink = new Sink();
            Session.Store = await HistoryStore.OpenAsync(Path.Combine(AppContext.BaseDirectory, "fixture-" + Guid.NewGuid().ToString("N") + ".db"));
            var main = new MainWindow(); typeof(App).GetProperty(nameof(Window))!.SetValue(this, main); main.Activate();
            var startup = new WorkspaceLayout { MainVisible = false, Windows = [new ChatWindowState {
                Source = "startup", OwnerKey = "unassigned:startup", ChannelId = tab.Id, Name = "Restored channel", Open = true,
                PendingDraft = "interrupted send", Bounds = new(999999, -999999, 600, 600),
            }] };
            await Session.Store.SaveLayoutAsync("current", startup);
            await Workspace.RestoreAsync(); await Until(() => main.Content.XamlRoot != null, "Main XamlRoot");
            var restoredStartup = Workspace.Popouts.Single();
            Check(!Workspace.MainVisible && restoredStartup.Composer.Text == "interrupted send" && restoredStartup.State.PendingDraft == null,
                "Startup restores hidden-main layout and recovers interrupted sends as drafts");
            var startupBounds = WorkspaceWindows.BoundsOf(restoredStartup);
            Check(startupBounds == startupBounds.Fit(WorkspaceWindows.WorkArea(startupBounds)), "Startup clamps a removed monitor position into a visible work area");
            Workspace.ShowMain(); await Workspace.ClosePopoutAsync(restoredStartup);
            Check(Workspace.MainVisible && Workspace.Popouts.Count == 0, "A restored popout can return to the visible main window");
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var keys = PublicKeyBox.GenerateKeyPair(); Config.TrustedKeys.Add(new TrustedKey("Workspace fixture", keys.PublicKey));
            var accept = listener.AcceptTcpClientAsync(); Connect("127.0.0.1", (ushort)((IPEndPoint)listener.LocalEndpoint).Port);
            using var peer = await accept; var stream = peer.GetStream(); await stream.ReadExactlyAsync(new byte[3]);
            var handshake = await KeyExchange.ServerHandshake(keys, stream); await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)); using var gate = new SemaphoreSlim(1);
            async Task Send(Encodable packet) { await gate.WaitAsync(timeout.Token); try { await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, packet, timeout.Token); } finally { gate.Release(); } }
            var commands = new List<ClientMessage>();
            async Task Serve() {
                try {
                    while (!timeout.IsCancellationRequested) {
                        var raw = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                        if (raw[0] == (byte)ClientOperation.Ping) await Send(Pong.Instance);
                        if (raw[0] == (byte)ClientOperation.Message) commands.Add(ClientMessage.Decode(raw[1..]));
                    }
                } catch (EndOfStreamException) { } catch (OperationCanceledException) { } catch (IOException) { }
            }
            _ = Serve();
            var owner = new CharacterIdentity { ContentId = 41, Name = "Workspace Tester", HomeWorldId = 1, HomeWorld = "Fixture" };
            var other = new CharacterIdentity { ContentId = 42, Name = "Other Character", HomeWorldId = 2, HomeWorld = "Second" };
            var target = new CharacterIdentity { ContentId = 51, Name = "Peer Friend", HomeWorldId = 3, HomeWorld = "PeerWorld" };
            PlayerData Player(CharacterIdentity who, string epoch) => new(who.HomeWorld, who.HomeWorld, "Test area", who.Name) { Identity = who, OwnerEpoch = epoch };
            await Send(new ServerCapabilities { ServiceId = "workspace", RunId = "run", GuardedCommands = true, DirectedTell = true });
            await Send(Player(owner, "login1")); await Send(new Availability(true)); await Send(new ServerChannel(InputChannel.Say, "Say") { Revision = 11 });
            await Until(() => Connection?.Available == true && Session.Player?.OwnerEpoch == "login1" && Connection.CurrentChannel == "Say", "Connected owner/channel");
            var source = Session.Source; var conn = Connection;
            var start = DateTime.UtcNow.AddHours(-1);
            ServerMessage Message(int n, CharacterIdentity? who = null) => new(start.AddSeconds(n), ChatType.TellIncoming,
                Encoding.UTF8.GetBytes(target.Name), Encoding.UTF8.GetBytes("采购记录 / chat " + n), [new TextChunk("采购记录 / chat " + n)]) {
                    Owner = who ?? owner, TellPeer = target, MessageId = "workspace:run:" + n, ServiceId = "workspace", RunId = "run", Sequence = n,
                };
            for (int offset = 0; offset < 700; offset += 100) await Task.WhenAll(Enumerable.Range(offset + 1, 100).Select(n => {
                var message = Message(n); return Session.Store.AppendAsync(source, message, HistoryStore.StorageId(source, message));
            }));
            var conversation = Workbench.Open(target)!;
            main.ReturnPopout(new ChatWindowState { Source = source, OwnerKey = owner.Key!, Peer = target });
            Control<TextBox>(main, "Composer").Text = "main-only draft";
            var popout = main.PopoutCurrent()!;
            await Until(() => popout.LoadedMessages.Count >= 500, "Popout history");
            Check(ReferenceEquals(popout, main.PopoutCurrent()) && Workspace.Popouts.Count == 1, "Repeated popout entry activates one window per view");
            Check(popout.Composer.Text == "" && conversation.Draft == "main-only draft", "New popout has a separate draft from the main conversation");
            popout.Composer.Text = "popout-only draft";
            Check(conversation.Draft == "main-only draft", "Typing in a popout leaves the main draft intact");
            Check(popout.CanSend, "Current owner popout enables guarded sending");
            popout.MessageView.ScrollToMessage(popout.LoadedMessages.First());
            popout.MessageView.RestoreScroll(null, true); await Task.Delay(500);
            Check(popout.MessageView.FollowingLatest, "A queued old history jump cannot override a later return-to-latest request");
            Check(popout.Submit(), "Popout queues a directed Tell"); await Until(() => commands.Count == 1, "Tell wire");
            Check(commands[0].TellTarget == TellTarget.From(target) && commands[0].ExpectedOwnerKey == owner.Key && commands[0].ExpectedOwnerEpoch == "login1", "Popout Tell keeps exact peer, owner and login on the wire");
            Check(conversation.Draft == "main-only draft" && popout.Composer.Text == "" && popout.State.PendingDraft == "popout-only draft", "Queued Tell preserves main draft and persists recoverable pending text");
            popout.Composer.Text = "new popout draft";
            Check(!popout.Submit() && commands.Count == 1, "One pending send per popout prevents losing multiple failed drafts");
            await Workspace.SaveAsync("Social fixture"); var snapshot = Workspace.Capture();
            await Workspace.ApplyAsync(snapshot); popout = Workspace.Popouts.Single();
            await Until(() => popout.LoadedMessages.Count >= 500, "Recreated popout");
            await Send(new ServerCommandResult { RequestId = commands[0].RequestId, Stage = CommandStage.Rejected, Failure = CommandFailure.IdentityChanged });
            await Until(() => popout.State.PendingDraft == null && popout.State.FailedDraft != null, "Late failed send after layout apply");
            Check(popout.Composer.Text == "new popout draft" && popout.State.FailedDraft == "popout-only draft" && conversation.Draft == "main-only draft", "Late failure follows the recreated popout and preserves both drafts");
            popout.State.FailedDraft = null; popout.Composer.Text = "accepted after restore";
            Check(popout.Submit(), "Recreated popout can send"); await Until(() => commands.Count == 2, "Second Tell");
            await Send(new ServerCommandResult { RequestId = commands[1].RequestId, Stage = CommandStage.Submitted });
            await Until(() => popout.State.PendingDraft == null, "Submitted receipt");
            Check(popout.Composer.Text == "" && conversation.Draft == "main-only draft", "Successful popout receipt does not alter main draft");

            main.Navigate("history");
            popout.MessageView.ScrollToMessage(popout.LoadedMessages.First()); await Task.Delay(200); popout.Capture();
            var scrollId = popout.State.ScrollId;
            Check(!popout.State.FollowingLatest && scrollId != null, "Popout captures a stable history reading anchor");
            await Workspace.SaveAsync(); var storedLayout = (await Session.Store.LoadLayoutAsync("current"))!;
            popout.Composer.Text = "keep newest draft";
            await Workspace.ApplyAsync(storedLayout); popout = Workspace.Popouts.Single();
            await Until(() => popout.LoadedMessages.Count >= 500 && !popout.MessageView.FollowingLatest && !popout.MessageView.ScrollInProgress, "Reading anchor restored");
            Check(popout.Composer.Text == "keep newest draft", "Applying an old layout preserves newer live drafts");
            popout.Capture(); Check(popout.State.ScrollId == scrollId, "Layout restores the same message anchor");
            var latest = Message(701); await Send(latest); await Until(() => popout.LoadedMessages.Any(m => m.MessageId == latest.MessageId), "Live shared message");
            Check(Workbench.Conversations.Single(c => c.Key == conversation.Key).Unread > 0, "Scrolled-back popout leaves new Tell unread");
            popout.MessageView.RestoreScroll(null, true); popout.BringForward(); await Task.Delay(500);
            await Send(Message(702)); await Until(() => Workbench.Conversations.Single(c => c.Key == conversation.Key).Unread == 0, "Shared read status");
            Check(popout.LoadedMessages.Count(m => m.MessageId == latest.MessageId) == 1, "SQLite history and live stream deduplicate in the shared view");
            var notification = new NotificationTarget(NotificationTargetKind.Conversation, source, owner.Key!, HistoryStore.StorageId(source, latest), target);
            await Until(() => popout.LoadedMessages.Any(m => m.Sequence == 702), "Last Tell rendered");
            await Task.Delay(200);
            if (!Workspace.IsReading(notification)) await Capture(popout, "workspace-reading-failure.png");
            Check(Workspace.IsReading(notification), "Active popout reading suppresses duplicate notifications; follow=" + popout.MessageView.FollowingLatest +
                "; matches=" + popout.Matches(notification) + "; active=" + typeof(ChatPopoutWindow).GetField("active", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(popout));
            main.Close(); await Until(() => !Workspace.MainVisible, "Main window hidden");
            Check(ReferenceEquals(Connection, conn) && Connection?.Available == true && Session.Store != null, "Closing main retains the shared connection while a chat popout remains");
            await Workspace.OpenNotificationAsync(notification);
            Check(!Workspace.MainVisible && Workspace.Popouts.Count == 1, "Notification click routes into the existing popout without reopening main");
            Workspace.ShowMain(); Check(Workspace.MainVisible && ReferenceEquals(Connection, conn), "Return-main reuses the same window and connection");

            await Send(Player(other, "login2")); await Until(() => Session.Player?.Identity?.Key == other.Key && !popout.CanSend, "Role changed");
            popout.Composer.Text = "old role private draft"; var before = commands.Count;
            Check(!popout.Submit() && commands.Count == before, "Old-role popout cannot send through the new role");
            int oldCount = popout.LoadedMessages.Count; await Send(Message(703, other)); await Task.Delay(250);
            Check(popout.LoadedMessages.Count == oldCount && popout.Composer.Text == "old role private draft", "New-role messages and typing cannot retarget the old window");
            main.Navigate("channels"); var channel = main.PopoutCurrent()!;
            Check(Workspace.Popouts.Count == 2 && channel.State.OwnerKey == other.Key, "Different owners get distinct popouts");
            channel.Composer.Text = "channel command"; Check(channel.Submit(), "Channel popout sends through guarded current game channel");
            await Until(() => commands.Count == before + 1, "Channel wire"); var channelCommand = commands.Last();
            Check(channelCommand.TellTarget == null && channelCommand.ExpectedOwnerKey == other.Key && channelCommand.ExpectedChannelRevision == 11, "Channel popout carries owner and current-channel revision");
            await Send(new ServerCommandResult { RequestId = channelCommand.RequestId, Stage = CommandStage.Rejected, Failure = CommandFailure.ChannelChanged });
            await Until(() => channel.Composer.Text == "channel command", "Channel failed draft recovered");
            Check(popout.Composer.Text == "old role private draft", "Channel failure recovery is confined to its own composer");
            await Send(Player(owner, "login3")); await Until(() => Session.Player?.Identity?.Key == owner.Key && popout.CanSend, "Original role rebound");
            Check(popout.Composer.Text == "old role private draft" && !channel.CanSend, "Returning to an owner rebinds its conversation without changing either draft");
            await Workspace.ClosePopoutAsync(channel);
            Check(Workspace.Popouts.Count == 1 && Connection?.Available == true, "Closing one popout leaves remaining chat windows connected");

            popout.State.Topmost = true; popout.State.Timestamps = false; popout.State.Markdown = false; popout.State.FontSize = 20;
            popout.State.Bounds = new WindowBounds(999999, -999999, 1500, 1200);
            var layout = Workspace.Capture(); layout.Windows.Single(w => w.Open).Bounds = new(999999, -999999, 1500, 1200);
            await Workspace.ApplyAsync(layout); popout = Workspace.Popouts.Single();
            await Task.Delay(250);
            var bounds = WorkspaceWindows.BoundsOf(popout); var work = WorkspaceWindows.WorkArea(bounds);
            Check(bounds == bounds.Fit(work), "Out-of-range restored windows are clamped inside the current work area");
            Check(((OverlappedPresenter)popout.AppWindow.Presenter).IsAlwaysOnTop && !Config.AlwaysOnTop && !popout.State.Timestamps && popout.State.FontSize == 20 && Config.FontSize == 14, "Popout topmost and display settings remain independent of main configuration");
            Workspace.ApplyPreset(0); Check(Workspace.Popouts.Count == 1, "Social preset arranges existing views without duplication");
            Workspace.ApplyPreset(1); Check(Workspace.MainVisible, "Duty preset keeps main reachable");
            Workspace.ApplyPreset(2); await Workspace.SaveAsync("Housing fixture");
            Check((await Session.Store!.GetLayoutNamesAsync()).Count == 2, "Named layouts save independently of startup layout");

            var export = new Export(popout.ExportQuery, "Peer Friend · full history"); export.Activate(); await Task.Delay(500);
            Check(Control<ListView>(export, "PreviewList").Items.Count <= 100 && export.Query().OwnerKey == owner.Key && export.Query().PeerKey == conversation.Key, "Export preview stays bounded and retains the original conversation scope");
            await Capture(export, "workspace-export-en.png");
            var path = Path.Combine(AppContext.BaseDirectory, "export-fixture.txt"); File.WriteAllText(path, new string('z', 200000));
            var file = await StorageFile.GetFileFromPathAsync(path);
            var count = await Export.WriteFileAsync(Session.Store, file, popout.ExportQuery, true);
            Check(count > 500 && File.ReadAllText(path).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length == count && !File.ReadAllText(path).Contains("zzzz"), "Transactional TXT export writes all history and truncates previous trailing contents");
            var rtfPath = Path.Combine(AppContext.BaseDirectory, "export-fixture.rtf"); File.WriteAllText(rtfPath, "old");
            var rtfFile = await StorageFile.GetFileFromPathAsync(rtfPath);
            await Export.WriteFileAsync(Session.Store, rtfFile, popout.ExportQuery, false);
            var rtf = File.ReadAllText(rtfPath); Check(rtf.StartsWith("{\\rtf1") && rtf.EndsWith("}") && rtf.Contains("\\u"), "RTF export produces a real Unicode rich-text document");
            File.WriteAllText(path, "original must survive cancellation"); using var cancel = new CancellationTokenSource();
            try { await Export.WriteFileAsync(Session.Store, file, popout.ExportQuery, true, progress: new SyncProgress(_ => cancel.Cancel()), token: cancel.Token); throw new Exception("Cancelled export committed"); }
            catch (OperationCanceledException) { Check(File.ReadAllText(path) == "original must survive cancellation", "Cancellation after streamed rows preserves the original destination file"); }
            LocalizationHelper.ApplyLanguage(AppLanguage.ChineseSimplified); await Task.Delay(150);
            await Capture(popout, "workspace-popout-zh.png"); await Capture(main, "workspace-main-zh.png");
            Check(popout.Title.Contains("Peer Friend") && Control<TextBlock>(export, "Explanation").Text.Contains("本地数据库"), "Open popout and export windows relocalize into Chinese");
            var layoutsWindow = new WorkspaceLayoutWindow(this); layoutsWindow.Activate(); await Task.Delay(200); await Capture(layoutsWindow, "workspace-layouts-zh.png");

            bool auxiliaryClosed = false; export.Closed += (_, _) => auxiliaryClosed = true;
            main.Close(); await Until(() => !Workspace.MainVisible, "Hide main before final close");
            main.Closed += (_, _) => {
                try {
                    Check(Workspace.Stopping && !Connected && Session.Store == null, "Last chat window stops connection and disposes history before main closes");
                    Check(Workspace.Popouts.Count == 0 && auxiliaryClosed, "Auxiliary windows do not keep the application alive");
                    results.Add("All workspace checks completed"); File.WriteAllLines(output, results);
                } catch (Exception ex) { results.Add("FAIL " + ex); File.WriteAllLines(output, results); Environment.Exit(1); }
            };
            await Workspace.ClosePopoutAsync(popout);
        } catch (Exception ex) { results.Add("FAIL " + ex); File.WriteAllLines(output, results); Environment.Exit(1); }
    }
}
