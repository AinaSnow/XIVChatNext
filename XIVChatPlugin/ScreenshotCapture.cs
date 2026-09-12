using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using XIVChatCommon;

namespace XIVChatPlugin {
    /// <summary>Only the Draw callback touches ImGui. Encoding runs asynchronously, with owned textures.</summary>
    internal sealed class ScreenshotCapture : IDisposable {
        private readonly Plugin plugin;
        private readonly object gate = new();
        private CaptureRequest? waiting;
        private bool disposed;
        private sealed record CaptureRequest(CancellationToken Token, TaskCompletionSource<Task<IDalamudTextureWrap>> Completion);
        internal ScreenshotCapture(Plugin plugin) { this.plugin = plugin; plugin.Interface.UiBuilder.Draw += this.Draw; }

        internal async Task<ScreenshotImage> CaptureAsync(ScreenshotQuality quality, CancellationToken token) {
            var completion = new TaskCompletionSource<Task<IDalamudTextureWrap>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new CaptureRequest(token, completion);
            lock (this.gate) {
                ObjectDisposedException.ThrowIf(this.disposed, this);
                if (this.waiting != null) throw new InvalidOperationException("A capture is already waiting for a frame.");
                this.waiting = request;
            }
            Task<IDalamudTextureWrap>? capture = null;
            try {
                capture = await completion.Task.WaitAsync(token).ConfigureAwait(false);
                // The SDK can leave FirstUpdateTask pending when rendering stops. WaitAsync supplies a real timeout;
                // the cleanup continuation owns any texture produced after cancellation.
                using var source = await AwaitTextureAsync(capture, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                long capturedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (source.Width is <= 0 or > 8192 || source.Height is <= 0 or > 8192 || (long)source.Width * source.Height > 33_554_432)
                    throw new InvalidOperationException("Unsupported capture dimensions.");
                double scale = Math.Min(1d, (double)ScreenshotProtocol.LongEdge(quality) / Math.Max(source.Width, source.Height));
                int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                using var resized = await AwaitTextureAsync(this.plugin.TextureProvider.CreateFromExistingTextureAsync(source,
                    new TextureModificationArgs { NewWidth = width, NewHeight = height, MakeOpaque = true },
                    leaveWrapOpen: true, debugName: "XIVChat screenshot resized", cancellationToken: token), token).ConfigureAwait(false);
                var jpeg = this.plugin.TextureReadback.GetSupportedImageEncoderInfos().FirstOrDefault(codec => codec.MimeTypes.Contains("image/jpeg"))
                    ?? throw new NotSupportedException("JPEG encoder unavailable.");
                using var output = new BoundedMemoryStream(ScreenshotProtocol.MaxImageBytes);
                await this.plugin.TextureReadback.SaveToStreamAsync(resized, jpeg.ContainerGuid, output,
                    new Dictionary<string, object> { ["ImageQuality"] = ScreenshotProtocol.JpegQuality(quality) },
                    leaveWrapOpen: true, leaveStreamOpen: true, cancellationToken: token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var bytes = output.ToArray();
                if (!ScreenshotProtocol.IsJpeg(bytes, width, height)) throw new InvalidOperationException("Encoder returned an invalid JPEG.");
                return new ScreenshotImage(bytes, width, height, capturedAt);
            } finally {
                lock (this.gate) { if (ReferenceEquals(this.waiting, request)) this.waiting = null; }
                // Draw may have won the race with cancellation before we received its task.
                if (capture == null && completion.Task.IsCompletedSuccessfully) _ = DisposeLateTextureAsync(completion.Task.Result);
            }
        }
        private static async Task<IDalamudTextureWrap> AwaitTextureAsync(Task<IDalamudTextureWrap> task, CancellationToken token) {
            try { return await task.WaitAsync(token).ConfigureAwait(false); }
            catch { _ = DisposeLateTextureAsync(task); throw; }
        }
        private static async Task DisposeLateTextureAsync(Task<IDalamudTextureWrap> task) {
            try { (await task.ConfigureAwait(false)).Dispose(); } catch { /* Observe late faults after cancellation. */ }
        }
        private void Draw() {
            lock (this.gate) {
                var request = this.waiting;
                if (request == null || request.Completion.Task.IsCompleted) return;
                if (request.Token.IsCancellationRequested) { request.Completion.TrySetCanceled(request.Token); return; }
                try {
                    var viewport = ImGui.GetMainViewport();
                    if (viewport.Size.X is <= 0 or > 8192 || viewport.Size.Y is <= 0 or > 8192 || viewport.Size.X * viewport.Size.Y > 33_554_432)
                        throw new InvalidOperationException("No valid game viewport is rendering.");
                    request.Completion.TrySetResult(this.plugin.TextureProvider.CreateFromImGuiViewportAsync(
                        new ImGuiViewportTextureArgs { ViewportId = viewport.ID, AutoUpdate = false, TakeBeforeImGuiRender = false },
                        debugName: "XIVChat screenshot frame", cancellationToken: request.Token));
                } catch (Exception ex) { request.Completion.TrySetException(ex); }
            }
        }
        public void Dispose() {
            this.plugin.Interface.UiBuilder.Draw -= this.Draw;
            lock (this.gate) { this.disposed = true; this.waiting?.Completion.TrySetCanceled(); this.waiting = null; }
        }
    }
}
