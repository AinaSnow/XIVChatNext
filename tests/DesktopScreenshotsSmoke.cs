using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
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
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop {
    internal static class DesktopScreenshotsSmokeProgram {
        [STAThread] public static void Main() {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(parameters => {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new DesktopScreenshotsSmokeApp();
            });
        }
    }
    internal sealed class DesktopScreenshotsSmokeApp : App {
        private readonly List<string> results = new();
        private void Check(bool condition, string label) { if (!condition) throw new Exception(label); this.results.Add("PASS " + label); }
        private static T Control<T>(Window window, string name) where T : FrameworkElement => (T)((FrameworkElement)window.Content).FindName(name);
        private static void Click(Window window, string name) => ((IInvokeProvider)new ButtonAutomationPeer(Control<Button>(window, name)).GetPattern(PatternInterface.Invoke)).Invoke();
        private static async Task Until(Func<bool> condition, int milliseconds = 15000) {
            for (int elapsed = 0; elapsed < milliseconds; elapsed += 50) { if (condition()) return; await Task.Delay(50); }
            throw new Exception("Screenshot UI condition timed out");
        }
        private async Task<byte[]> FixtureJpeg() {
            const int width = 1280, height = 720;
            var random = new Random(29); var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
                int i = (y * width + x) * 4, grain = random.Next(0, 50);
                pixels[i] = (byte)(50 + 130 * y / height + grain);
                pixels[i + 1] = (byte)(35 + 150 * x / width + grain);
                pixels[i + 2] = (byte)(35 + 120 * ((x / 160 + y / 90) % 2) + grain); pixels[i + 3] = 255;
            }
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels); await encoder.FlushAsync();
            var bytes = new byte[(int)stream.Size]; stream.Seek(0); using var reader = new DataReader(stream); await reader.LoadAsync((uint)bytes.Length); reader.ReadBytes(bytes);
            this.Check(bytes.Length > ScreenshotProtocol.ChunkBytes && ScreenshotProtocol.IsJpeg(bytes, width, height), "Native encoder fixture crosses chunk boundary and passes JPEG validation");
            return bytes;
        }
        private static ServerScreenshot[] Packets(ClientScreenshot request, byte[] jpeg) {
            int count = (jpeg.Length + ScreenshotProtocol.ChunkBytes - 1) / ScreenshotProtocol.ChunkBytes;
            var digest = SHA256.HashData(jpeg); long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return Enumerable.Range(0, count).Select(i => new ServerScreenshot {
                RequestId = request.RequestId, OwnerKey = request.OwnerKey, OwnerEpoch = request.OwnerEpoch, Status = ScreenshotStatus.Data,
                Width = 1280, Height = 720, CapturedAtUnixMilliseconds = timestamp, TotalBytes = jpeg.Length, ChunkCount = count, ChunkIndex = i,
                Data = jpeg.Skip(i * ScreenshotProtocol.ChunkBytes).Take(ScreenshotProtocol.ChunkBytes).ToArray(), Digest = digest,
            }).ToArray();
        }
        private async Task CaptureUi(Window window, string filename) {
            var capture = new RenderTargetBitmap(); await capture.RenderAsync((FrameworkElement)window.Content);
            var buffer = await capture.GetPixelsAsync(); using var reader = DataReader.FromBuffer(buffer); var pixels = new byte[buffer.Length]; reader.ReadBytes(pixels);
            var file = await StorageFile.GetFileFromPathAsync(CreateEmpty(filename));
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)capture.PixelWidth, (uint)capture.PixelHeight, 96, 96, pixels); await encoder.FlushAsync();
        }
        private static string CreateEmpty(string filename) { var path = Path.Combine(AppContext.BaseDirectory, filename); File.WriteAllBytes(path, []); return path; }

        protected override async void OnLaunched(LaunchActivatedEventArgs args) {
            try {
                typeof(App).GetProperty(nameof(Config))!.SetValue(this, new Configuration { OnlineAvatars = false, HistoryEnabled = false });
                LocalizationHelper.Initialize(AppLanguage.English);
                var main = new MainWindow(); typeof(App).GetProperty(nameof(Window))!.SetValue(this, main); main.Activate();
                var window = ScreenshotWindow.ShowScreenshot();
                await Until(() => Control<TextBlock>(window, "StateText").Text.Contains("Connect"));
                this.Check(!Control<Button>(window, "CaptureButton").IsEnabled && !Control<Button>(window, "SaveButton").IsEnabled, "Offline window explains connection requirement and disables capture/save");
                this.Check(ReferenceEquals(window, ScreenshotWindow.ShowScreenshot()), "Screenshot entry reuses one window");
                var jpeg = await this.FixtureJpeg();
                using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                var key = PublicKeyBox.GenerateKeyPair(); this.Config.TrustedKeys.Add(new TrustedKey("Screenshot fixture", key.PublicKey));
                var accepted = listener.AcceptTcpClientAsync(); this.Connect("127.0.0.1", (ushort)((IPEndPoint)listener.LocalEndpoint).Port);
                using var peer = await accepted; var stream = peer.GetStream(); await stream.ReadExactlyAsync(new byte[3]);
                var handshake = await KeyExchange.ServerHandshake(key, stream);
                var preferences = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx);
                this.Check(ClientPreferences.Decode(preferences[1..]).TryGetValue<bool>(ClientPreference.ScreenshotSupport, out var supported) && supported, "Encrypted client negotiates screenshot support");
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)); using var sendGate = new SemaphoreSlim(1);
                async Task Send(Encodable packet) {
                    await sendGate.WaitAsync(timeout.Token);
                    try { await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, packet, timeout.Token); } finally { sendGate.Release(); }
                }
                var owner = new CharacterIdentity { ContentId = 41, Name = "Screenshot Tester", HomeWorldId = 1, HomeWorld = "Fixture" };
                PlayerData Player(CharacterIdentity who, string epoch) => new("Fixture", "Fixture", "Test area", who.Name) { Identity = who, OwnerEpoch = epoch };
                var requests = new List<ClientScreenshot>(); var delayed = new List<ClientScreenshot>(); int mode = 0;
                async Task Serve() {
                    try {
                        while (!timeout.IsCancellationRequested) {
                            var bytes = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, timeout.Token);
                            if (bytes[0] == (byte)ClientOperation.Ping) { await Send(Pong.Instance); continue; }
                            if (bytes[0] != (byte)ClientOperation.Screenshot) continue;
                            var request = ClientScreenshot.Decode(bytes[1..]); requests.Add(request);
                            if (request.Cancel) continue;
                            if (mode is 1 or 2 or 4) { delayed.Add(request); continue; }
                            await Send(new ServerScreenshot { RequestId = request.RequestId, OwnerKey = request.OwnerKey, OwnerEpoch = request.OwnerEpoch, Status = ScreenshotStatus.Capturing });
                            var packets = Packets(request, jpeg);
                            if (mode == 3) {
                                await Send(packets[0]); packets[0].Data[100] ^= 1; await Send(packets[0]); continue;
                            }
                            for (int i = packets.Length - 1; i >= 0; i--) {
                                await Send(packets[i]); if (i == packets.Length - 1) await Send(packets[i]);
                                await Task.Delay(20);
                            }
                        }
                    } catch (OperationCanceledException) { } catch (EndOfStreamException) { } catch (IOException) when (timeout.IsCancellationRequested) { }
                }
                var server = Serve();
                try {
                    await Send(new ServerCapabilities { ServiceId = "screenshots", RunId = "run" });
                    await Send(Player(owner, "login1")); await Send(new Availability(true));
                    await Until(() => Connection?.Available == true && Session.Player?.OwnerEpoch == "login1");
                    await Until(() => Control<TextBlock>(window, "StateText").Text.Contains("does not support"));
                    this.Check(!Control<Button>(window, "CaptureButton").IsEnabled, "Old plugin shows upgrade guidance and does not send screenshot requests");
                    await Send(new ServerCapabilities { ServiceId = "screenshots", RunId = "run", Screenshots = true });
                    await Until(() => this.Screenshots.CanCapture && Control<Button>(window, "CaptureButton").IsEnabled);
                    Click(window, "CaptureButton"); await Until(() => this.Screenshots.Busy);
                    this.Check(Control<Button>(window, "CancelButton").IsEnabled && !Control<Button>(window, "CaptureButton").IsEnabled, "Capture reserves one UI request and enables cancellation");
                    await Until(() => this.Screenshots.Preview != null && !this.Screenshots.Busy && Control<Image>(window, "PreviewImage").Source != null);
                    var preview = this.Screenshots.Preview!;
                    this.Check(preview.Image.Bytes.SequenceEqual(jpeg) && preview.OwnerKey == owner.Key && preview.OwnerEpoch == "login1", "Encrypted out-of-order transfer preserves complete JPEG and captured identity");
                    this.Check(requests.First(r => !r.Cancel).Quality == ScreenshotQuality.Standard, "Standard quality is sent by default");
                    this.Check(Control<TextBlock>(window, "SourceText").Text.Contains("Screenshot Tester @ Fixture") && Control<TextBlock>(window, "DetailsText").Text.Contains("1280 × 720"), "Preview shows original character, dimensions and capture time");
                    this.Check(Control<Button>(window, "SaveButton").IsEnabled, "Complete native JPEG enables save");
                    Click(window, "ActualButton"); await Task.Delay(80); var viewport = Control<ScrollViewer>(window, "Viewport");
                    this.Check(Math.Abs(viewport.ZoomFactor - 1) < .01, "100 percent view uses original pixels");
                    Click(window, "ZoomInButton"); await Task.Delay(80); this.Check(viewport.ZoomFactor > 1, "Preview zooms in");
                    Click(window, "FitButton"); await Task.Delay(80); this.Check(viewport.ZoomFactor <= 1, "Fit restores bounded image scale");
                    var saved = await StorageFile.GetFileFromPathAsync(CreateEmpty("screenshot-saved-fixture.jpg"));
                    await ScreenshotWindow.SaveImageAsync(preview.Image, saved);
                    this.Check(File.ReadAllBytes(saved.Path).SequenceEqual(jpeg), "Transactional save writes exactly the received JPEG");
                    LocalizationHelper.ApplyLanguage(AppLanguage.ChineseSimplified); await Task.Delay(100);
                    this.Check(Control<Button>(window, "CaptureButton").Content?.ToString() == "截取画面" && Control<TextBlock>(window, "StateText").Text.Contains("已完成"), "Open screenshot window relocalizes into Chinese");
                    this.Check(Control<ComboBox>(window, "QualityPicker").SelectionBoxItem?.ToString() == LocalizationHelper.GetString("Screenshot.Standard"), "Collapsed quality selection refreshes after language change");
                    await this.CaptureUi(window, "screenshots-preview-zh.png");
                    LocalizationHelper.ApplyLanguage(AppLanguage.English); await this.CaptureUi(window, "screenshots-preview-en.png");

                    mode = 1; var before = requests.Count; Click(window, "CaptureButton");
                    await Until(() => requests.Skip(before).Any(r => !r.Cancel));
                    Click(window, "CancelButton"); await Until(() => !this.Screenshots.Busy && requests.Skip(before).Any(r => r.Cancel));
                    this.Check(this.Screenshots.StateKey == "Screenshot.Cancelled" && ReferenceEquals(this.Screenshots.Preview, preview), "Cancellation keeps previous preview and sends bounded cancel request");
                    foreach (var packet in Packets(delayed.Last(), jpeg)) await Send(packet);
                    await Task.Delay(120); this.Check(ReferenceEquals(this.Screenshots.Preview, preview), "Cancelled late chunks cannot replace preview");

                    mode = 2; before = requests.Count; Click(window, "CaptureButton"); await Until(() => requests.Skip(before).Any(r => !r.Cancel));
                    var other = new CharacterIdentity { ContentId = 42, Name = "Second Tester", HomeWorldId = 1, HomeWorld = "Fixture" };
                    await Send(Player(other, "login2")); await Until(() => Session.Player?.OwnerEpoch == "login2" && !Screenshots.Busy);
                    foreach (var packet in Packets(delayed.Last(), jpeg)) await Send(packet);
                    await Task.Delay(120); this.Check(ReferenceEquals(Screenshots.Preview, preview) && !Screenshots.PreviewIsCurrent, "Role change cancels pending transfer and labels old preview");
                    this.Check(Control<TextBlock>(window, "SourceText").Text.Contains("Screenshot Tester") && Control<TextBlock>(window, "SourceText").Text.Contains("Previous"), "Old image never adopts new character name");

                    mode = 3; await Screenshots.CaptureAsync(ScreenshotQuality.Standard);
                    this.Check(Screenshots.StateKey == "Screenshot.Unavailable" && ReferenceEquals(Screenshots.Preview, preview) && Connection?.Available == true, "Conflicting chunk fails capture while retaining chat connection and prior image");
                    mode = 0; Control<ComboBox>(window, "QualityPicker").SelectedIndex = 1; before = requests.Count; Click(window, "CaptureButton");
                    await Until(() => !Screenshots.Busy && Screenshots.Preview?.OwnerKey == other.Key);
                    this.Check(requests.Skip(before).Any(r => !r.Cancel && r.Quality == ScreenshotQuality.Detailed), "Detailed quality requests and captures the new character");

                    mode = 4; before = requests.Count; Click(window, "CaptureButton"); await Until(() => requests.Skip(before).Any(r => !r.Cancel));
                    var priorLoginPreview = Screenshots.Preview;
                    await Send(Player(other, "login3")); await Until(() => Session.Player?.OwnerEpoch == "login3" && !Screenshots.Busy);
                    foreach (var packet in Packets(delayed.Last(), jpeg)) await Send(packet);
                    await Task.Delay(100);
                    this.Check(ReferenceEquals(Screenshots.Preview, priorLoginPreview) && !Screenshots.PreviewIsCurrent, "Same character with a new login epoch fences previous transfer");

                    before = requests.Count; Click(window, "CaptureButton"); await Until(() => requests.Skip(before).Any(r => !r.Cancel));
                    window.Close(); await Until(() => requests.Skip(before).Any(r => r.Cancel));
                    foreach (var packet in Packets(delayed.Last(), jpeg)) await Send(packet);
                    await Task.Delay(100);
                    this.Check(Screenshots.Preview == null && !Screenshots.Busy && Connection?.Available == true, "Closing during capture cancels transfer and discards late image without closing chat");
                    window = ScreenshotWindow.ShowScreenshot(); mode = 0;
                    await Until(() => Control<Button>(window, "CaptureButton").IsEnabled);
                    Click(window, "CaptureButton"); await Until(() => Screenshots.Preview != null && !Screenshots.Busy && Control<Image>(window, "PreviewImage").Source != null);

                    mode = 4; before = requests.Count; Click(window, "CaptureButton"); await Until(() => requests.Skip(before).Any(r => !r.Cancel));
                    await Send(new Availability(false)); await Send(EmptyPlayerData.Instance);
                    await Until(() => !Screenshots.Busy && !Screenshots.CanCapture);
                    this.Check(!Screenshots.PreviewIsCurrent && Control<Button>(window, "SaveButton").IsEnabled, "Logout cancels capture but permits saving the explicitly old image");
                    foreach (var packet in Packets(delayed.Last(), jpeg)) await Send(packet);
                    await Task.Delay(100); this.Check(!Screenshots.PreviewIsCurrent, "Late logout image cannot become current");
                    window.Close(); this.Check(Screenshots.Preview == null && !Screenshots.Busy, "Closing screenshot window releases image and request");
                    this.Check(Connection?.Available == false && this.Connected, "Closing preview does not close the main connection");
                    window = ScreenshotWindow.ShowScreenshot(); await Task.Delay(100);
                    this.Check(Control<Image>(window, "PreviewImage").Source == null, "Reopened preview does not retain discarded image bytes");
                    await Send(Player(other, "login4")); await Send(new Availability(true));
                    await Until(() => Control<Button>(window, "CaptureButton").IsEnabled);
                    before = requests.Count; Click(window, "CaptureButton"); await Until(() => requests.Skip(before).Any(r => !r.Cancel));
                    peer.Client.Shutdown(SocketShutdown.Send);
                    await Until(() => !this.Connected && !Screenshots.Busy);
                    this.Check(Screenshots.Preview == null && !Screenshots.CanCapture && !Control<Button>(window, "CaptureButton").IsEnabled, "Network EOF cancels active capture and disables further requests");
                    await this.StopSessionAsync();
                    this.Check((bool)typeof(ScreenshotWindow).GetField("closed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!, "Main application shutdown closes the screenshot window");
                } finally { timeout.Cancel(); this.Disconnect(); try { await server; } catch (IOException) { } }
                this.results.Add("All screenshot checks completed");
                File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "screenshots-smoke-results.txt"), this.results);
                await this.StopSessionAsync(); this.Exit();
            } catch (Exception ex) {
                this.results.Add("FAIL " + ex); File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "screenshots-smoke-results.txt"), this.results);
                Environment.Exit(1);
            }
        }
    }
}
