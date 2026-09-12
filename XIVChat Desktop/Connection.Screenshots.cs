using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XIVChatCommon;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop {
    public sealed class ScreenshotException(ScreenshotStatus status) : Exception(status.ToString()) {
        public ScreenshotStatus Status { get; } = status;
    }
    public sealed record ScreenshotProgress(int Received, int Total);

    public partial class Connection {
        private readonly object screenshotGate = new();
        private PendingScreenshot? pendingScreenshot;
        private sealed class PendingScreenshot(ClientScreenshot request, IProgress<ScreenshotProgress>? progress) {
            internal readonly ClientScreenshot Request = request;
            internal readonly ScreenshotAssembler Assembler = new(request.RequestId, request.OwnerKey, request.OwnerEpoch);
            internal readonly TaskCompletionSource<ScreenshotImage> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly IProgress<ScreenshotProgress>? Progress = progress;
        }
        public bool SupportsScreenshots => this.capabilities?.Screenshots == true;
        private bool ScreenshotOwnerMatches(ClientScreenshot request) => this.Available && ReferenceEquals(this.app.Connection, this) &&
            !this.cancel.IsCancellationRequested && this.commandPlayer?.Identity?.Key == request.OwnerKey && this.commandPlayer.OwnerEpoch == request.OwnerEpoch;

        public async Task<ScreenshotImage> RequestScreenshotAsync(ScreenshotQuality quality, string owner, string epoch,
            IProgress<ScreenshotProgress>? progress, CancellationToken token) {
            var request = new ClientScreenshot { RequestId = Guid.NewGuid().ToString("N"), OwnerKey = owner, OwnerEpoch = epoch, Quality = quality };
            if (!request.Valid || !this.SupportsScreenshots || !this.ScreenshotOwnerMatches(request)) throw new ScreenshotException(ScreenshotStatus.Unavailable);
            var pending = new PendingScreenshot(request, progress);
            lock (this.screenshotGate) {
                if (this.pendingScreenshot != null) throw new ScreenshotException(ScreenshotStatus.Busy);
                this.pendingScreenshot = pending;
            }
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, this.cancel.Token);
            lifetime.CancelAfter(TimeSpan.FromSeconds(ScreenshotProtocol.TransferTimeoutSeconds + 5));
            bool complete = false;
            try {
                if (!this.QueuePacket(request.Encode())) throw new ScreenshotException(ScreenshotStatus.Unavailable);
                var image = await pending.Completion.Task.WaitAsync(lifetime.Token).ConfigureAwait(false);
                if (!this.ScreenshotOwnerMatches(request)) throw new ScreenshotException(ScreenshotStatus.Stale);
                complete = true; return image;
            } catch (OperationCanceledException) when (!token.IsCancellationRequested && !this.cancel.IsCancellationRequested) {
                throw new ScreenshotException(ScreenshotStatus.TimedOut);
            } finally {
                lock (this.screenshotGate) { if (ReferenceEquals(this.pendingScreenshot, pending)) this.pendingScreenshot = null; }
                if (!complete && !this.cancel.IsCancellationRequested) {
                    request.Cancel = true;
                    // Best effort cancellation must not disconnect chat when its ordinary queue is already busy.
                    this.outgoingMessages.Writer.TryWrite(request.Encode());
                }
            }
        }
        private void InvalidateScreenshot() {
            lock (this.screenshotGate) {
                if (this.pendingScreenshot is { } pending && !this.ScreenshotOwnerMatches(pending.Request))
                    pending.Completion.TrySetException(new ScreenshotException(ScreenshotStatus.Stale));
            }
        }
        private void ReceiveScreenshot(ServerScreenshot reply) {
            lock (this.screenshotGate) {
                var pending = this.pendingScreenshot;
                if (pending == null || pending.Completion.Task.IsCompleted || reply.RequestId != pending.Request.RequestId) return;
                if (!reply.Valid || reply.OwnerKey != pending.Request.OwnerKey || reply.OwnerEpoch != pending.Request.OwnerEpoch ||
                    !this.ScreenshotOwnerMatches(pending.Request)) {
                    pending.Completion.TrySetException(new ScreenshotException(ScreenshotStatus.Stale)); return;
                }
                if (reply.Status == ScreenshotStatus.Capturing) { pending.Progress?.Report(new(0, 0)); return; }
                if (reply.Status != ScreenshotStatus.Data) { pending.Completion.TrySetException(new ScreenshotException(reply.Status)); return; }
                try {
                    if (reply.Width > ScreenshotProtocol.LongEdge(pending.Request.Quality) || reply.Height > ScreenshotProtocol.LongEdge(pending.Request.Quality))
                        throw new InvalidDataException("Screenshot exceeds requested dimensions.");
                    var image = pending.Assembler.Add(reply);
                    pending.Progress?.Report(new(pending.Assembler.ReceivedBytes, pending.Assembler.TotalBytes));
                    if (image != null) pending.Completion.TrySetResult(image);
                } catch (InvalidDataException) { pending.Completion.TrySetException(new ScreenshotException(ScreenshotStatus.Unavailable)); }
            }
        }
    }
}
