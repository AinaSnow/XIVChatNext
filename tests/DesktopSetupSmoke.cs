using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Sodium;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using XIVChatCommon;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop;

internal static class DesktopSetupSmokeProgram {
    [STAThread] public static void Main() {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters => { SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread())); _ = new DesktopSetupSmokeApp(); });
    }
}
internal sealed class DesktopSetupSmokeApp : App {
    private readonly List<string> results = new();
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static void Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args);
    private void Check(bool ok, string message) { if (!ok) throw new Exception(message); results.Add("PASS " + message); File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "setup-smoke-results.txt"), results); }
    private static async Task Click(Button button) { ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(80); }
    private static async Task Until(Func<bool> predicate) { for (var i = 0; i < 120; i++) { if (predicate()) return; await Task.Delay(50); } throw new Exception("Condition did not complete."); }
    private sealed class Sink : INotificationSink { public List<NotificationDelivery> Sent { get; } = new(); public bool Show(NotificationDelivery delivery) { Sent.Add(delivery); return true; } public void Dispose() { } }
    private static async Task Capture(Window window, string name) {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync((FrameworkElement)window.Content);
        var buffer = await bitmap.GetPixelsAsync(); using var reader = DataReader.FromBuffer(buffer); var pixels = new byte[buffer.Length]; reader.ReadBytes(pixels);
        var path = Path.Combine(AppContext.BaseDirectory, name); File.WriteAllBytes(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels); await encoder.FlushAsync();
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args) {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "setup-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixtures);
        var config = new Configuration { OnlineAvatars = false, BacklogMessages = 0, FilePathOverride = Path.Combine(fixtures, "config.json") };
        typeof(App).GetProperty(nameof(Config))!.SetValue(this, config);
        var sink = new Sink(); Notifier.Sink = sink;
        try {
            Check(config.PrepareSetup(false, false), "new configuration opens the initial wizard");
            Check(config.PrepareSetup(true, false), "interrupted first-run setup resumes after restart");
            Check(!new Configuration().PrepareSetup(true, false), "legacy users keep their settings without a forced wizard");
            Check(!new Configuration().PrepareSetup(false, true), "backup recovery takes precedence over first-run setup");
            config.Save();
            LocalizationHelper.Initialize(AppLanguage.English);
            var main = new MainWindow(); typeof(App).GetProperty(nameof(Window))!.SetValue(this, main); main.Activate(); await Task.Delay(200);
            var wizard = new SetupWizard(); wizard.Activate(); await Task.Delay(150);
            Field<ComboBox>(wizard, "language").SelectedIndex = (int)AppLanguage.ChineseSimplified; await Task.Delay(100);
            Check(wizard.Title == "初始设置" && Config.Language == AppLanguage.System, "language previews immediately without saving the draft");
            await Capture(wizard, "setup-language-zh.png");
            await Click(Field<Button>(wizard, "next"));
            Field<TextBox>(wizard, "port").Text = "0"; await Click(Field<Button>(wizard, "next"));
            Check(Field<int>(wizard, "step") == 1 && Field<TextBlock>(wizard, "status").Text.Length > 0, "invalid port is explained and cannot advance");
            Field<TextBox>(wizard, "port").Text = "14777";
            Field<ComboBox>(wizard, "location").SelectedIndex = 1;
            Check(Field<TextBox>(wizard, "host").Text == "", "another-computer mode does not retain the loopback address");
            Field<ComboBox>(wizard, "location").SelectedIndex = 2; await Click(Field<Button>(wizard, "next"));
            Check(Field<int>(wizard, "step") == 1, "relay setup requires a paired connection");
            Field<ComboBox>(wizard, "location").SelectedIndex = 0; await Capture(wizard, "setup-connection-zh.png");
            await Click(Field<Button>(wizard, "next"));
            Field<CheckBox>(wizard, "sound").IsChecked = false; await Click(Field<Button>(wizard, "notificationTest"));
            Check(sink.Sent.Count == 1 && !sink.Sent[0].Sound && Config.NotificationOptions.Sound, "test notification uses draft sound without changing saved preferences");
            Field<CheckBox>(wizard, "dnd").IsChecked = true; await Click(Field<Button>(wizard, "notificationTest"));
            Check(sink.Sent.Count == 1, "draft do-not-disturb suppresses the preview and reports it");
            await Capture(wizard, "setup-notifications-zh.png");
            Field<CheckBox>(wizard, "dnd").IsChecked = false;
            await Click(Field<Button>(wizard, "next")); Field<CheckBox>(wizard, "connect").IsChecked = false;
            var validPath = Config.FilePathOverride; Config.FilePathOverride = fixtures;
            await Click(Field<Button>(wizard, "next"));
            Check(Config.SetupVersion == 0 && Config.Servers.Count == 0 && Field<TextBlock>(wizard, "status").Text.Length > 0, "failed save rolls back settings and preserves the wizard for retry");
            Config.FilePathOverride = validPath; await Click(Field<Button>(wizard, "next"));
            Check(Config.SetupVersion == 1 && Config.Language == AppLanguage.ChineseSimplified && !Config.NotificationOptions.Sound && !Connected, "wizard commits language, notifications and direct profile without connecting when unchecked");
            var reloaded = (Configuration)typeof(Configuration).GetMethod("Deserialize", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { File.ReadAllText(validPath!) })!;
            Check(reloaded.SetupVersion == 1 && reloaded.Servers[0].Port == 14777, "saved setup survives configuration deserialization");
            var deferred = new SetupWizard(); deferred.Activate(); await Task.Delay(100); await Click(Field<Button>(deferred, "skip"));
            Check(Config.SetupDeferred && !Config.PrepareSetup(true, false), "deferred setup does not force itself on every launch");
            var pair = new RelayPairDialog(); pair.Activate(); await Task.Delay(100);
            Field<PasswordBox>(pair, "invitation").Password = "invalid"; await Click(Field<Button>(pair, "inspect"));
            Check(Field<TextBlock>(pair, "error").Text.Length > 0 && Config.Servers.Count == 1, "invalid relay invitation is rejected before saving any connection");
            await Capture(pair, "setup-relay-pair-zh.png"); pair.Close();
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using var stop = new CancellationTokenSource(); var key = PublicKeyBox.GenerateKeyPair();
            Config.TrustedKeys.Add(new TrustedKey("fixture", key.PublicKey));
            var target = new SavedServer("Fixture", "127.0.0.1", (ushort)((IPEndPoint)listener.LocalEndpoint).Port); Config.Servers.Add(target); Config.LastSuccessfulConnection = target.Snapshot();
            var accepted = 0;
            var server = Task.Run(async () => {
                try { while (!stop.IsCancellationRequested) {
                    var client = await listener.AcceptTcpClientAsync(stop.Token); Interlocked.Increment(ref accepted);
                    _ = Task.Run(async () => { using (client) { try {
                        var stream = client.GetStream(); await stream.ReadExactlyAsync(new byte[3], stop.Token); var h = await KeyExchange.ServerHandshake(key, stream, stop.Token);
                        await SecretMessage.ReadSecretMessage(stream, h.Keys.rx, stop.Token); await SecretMessage.SendSecretMessage(stream, h.Keys.tx, Pong.Instance, stop.Token);
                        while (!stop.IsCancellationRequested) await SecretMessage.ReadSecretMessage(stream, h.Keys.rx, stop.Token);
                    } catch (Exception) { } } });
                } } catch (OperationCanceledException) { }
            });
            Call(main, "QuickConnect_Click", main, new RoutedEventArgs()); await Until(() => Connection?.SessionReady == true);
            var id = Connection!.Id; Call(main, "QuickConnect_Click", main, new RoutedEventArgs()); await Task.Delay(100);
            Check(accepted == 1 && Connection?.Id == id, "Logo connects the recent target and repeated clicks do not duplicate or disconnect it");
            await Until(() => File.ReadAllText(validPath!).Contains("Fixture"));
            reloaded = (Configuration)typeof(Configuration).GetMethod("Deserialize", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { File.ReadAllText(validPath!) })!;
            Check(reloaded.LastSuccessfulConnection?.Port == target.Port, "successful connection persists the endpoint for next launch");
            Disconnect(); await Task.Delay(100);
            using var closedPort = new TcpListener(IPAddress.Loopback, 0); closedPort.Start(); var unavailable = (ushort)((IPEndPoint)closedPort.LocalEndpoint).Port; closedPort.Stop();
            Connect("127.0.0.1", unavailable); await Until(() => !Connected);
            Check(Config.LastSuccessfulConnection?.Id == target.Id, "failed connection does not overwrite the last successful endpoint");
            stop.Cancel(); listener.Stop(); await server;
            await DesktopRelaySmoke.Run(this, Check, (instance, name) => Field<object>(instance, name), Click);
            results.Add("All setup checks completed");
        } catch (Exception ex) { results.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally { File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "setup-smoke-results.txt"), results); Disconnect(); Exit(); }
    }
}
