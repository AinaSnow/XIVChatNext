using System;
using System.Threading;
using System.Threading.Tasks;
using XIVChatCommon;

namespace XIVChat_Desktop {
    public sealed record ScreenshotPreview(ScreenshotImage Image, string ConnectionId, string Source, string OwnerKey, string OwnerEpoch, string CharacterName);
    public sealed class ScreenshotSession {
        private readonly App app;
        private CancellationTokenSource? cancellation;
        private string context = "";
        private int version;
        public bool Busy { get; private set; }
        public ScreenshotPreview? Preview { get; private set; }
        public string StateKey { get; private set; } = "Screenshot.Ready";
        public ScreenshotProgress Progress { get; private set; } = new(0, 0);
        public event Action? Changed;
        public ScreenshotSession(App app) => this.app = app;
        public bool CanCapture => this.app.Connection is { Available: true, SupportsScreenshots: true } connection && !connection.cancel.IsCancellationRequested &&
            this.app.Session.Player?.Identity?.Key != null && !string.IsNullOrEmpty(this.app.Session.Player.OwnerEpoch);
        public bool PreviewIsCurrent => this.Preview is { } preview && this.IsCurrent(preview);
        public bool IsCurrent(ScreenshotPreview preview) => this.app.Connection is { Available: true } connection &&
            !connection.cancel.IsCancellationRequested && preview.ConnectionId == connection.Id && preview.OwnerKey == this.app.Session.Player?.Identity?.Key &&
            preview.OwnerEpoch == this.app.Session.Player?.OwnerEpoch;
        public void UpdateContext() {
            var connection = this.app.Connection; var player = this.app.Session.Player;
            var next = $"{connection?.Id}/{connection?.Available}/{connection?.SupportsScreenshots}/{player?.Identity?.Key}/{player?.OwnerEpoch}";
            if (next != this.context) {
                this.context = next; this.version++; this.cancellation?.Cancel();
                if (this.Busy) { this.Busy = false; this.StateKey = "Screenshot.Stale"; }
            }
            this.Changed?.Invoke();
        }
        public async Task CaptureAsync(ScreenshotQuality quality) {
            if (this.Busy || !this.CanCapture) return;
            var connection = this.app.Connection!; var player = this.app.Session.Player!;
            var owner = player.Identity!.Key!; var epoch = player.OwnerEpoch!;
            var character = player.Identity.Name + " @ " + player.Identity.HomeWorld;
            var cancel = this.cancellation = new CancellationTokenSource();
            int current = ++this.version;
            this.Busy = true; this.StateKey = "Screenshot.Capturing"; this.Progress = new(0, 0); this.Changed?.Invoke();
            try {
                var progress = new Progress<ScreenshotProgress>(value => {
                    if (current != this.version || cancel.IsCancellationRequested || !this.Busy) return;
                    this.Progress = value; this.StateKey = value.Total > 0 ? "Screenshot.Receiving" : "Screenshot.Capturing"; this.Changed?.Invoke();
                });
                var image = await connection.RequestScreenshotAsync(quality, owner, epoch, progress, cancel.Token);
                if (current != this.version || cancel.IsCancellationRequested || !ReferenceEquals(connection, this.app.Connection) ||
                    player.Identity.Key != this.app.Session.Player?.Identity?.Key || epoch != this.app.Session.Player?.OwnerEpoch) return;
                this.Preview = new(image, connection.Id, connection.Source, owner, epoch, character);
                this.StateKey = "Screenshot.Complete";
            } catch (ScreenshotException ex) { if (current == this.version) this.StateKey = "Screenshot." + ex.Status; }
            catch (OperationCanceledException) { if (current == this.version) this.StateKey = "Screenshot.Cancelled"; }
            catch { if (current == this.version) this.StateKey = "Screenshot.Unavailable"; }
            finally {
                if (current == this.version) { this.Busy = false; this.Changed?.Invoke(); }
                if (ReferenceEquals(this.cancellation, cancel)) this.cancellation = null;
                cancel.Dispose();
            }
        }
        public void Cancel() { this.cancellation?.Cancel(); }
        public void Close() {
            this.version++; this.cancellation?.Cancel(); this.Busy = false; this.Preview = null;
            this.Progress = new(0, 0); this.StateKey = "Screenshot.Ready"; this.Changed?.Invoke();
        }
    }
}
