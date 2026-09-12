using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using XIVChatCommon;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;

namespace XIVChatPlugin {
    /// <summary>One capture/transfer across all peers. No image queue and no native reads on network threads.</summary>
    internal sealed class ScreenshotCoordinator : IDisposable {
        private readonly object gate = new();
        private readonly Func<ScreenshotQuality, CancellationToken, Task<ScreenshotImage>> capture;
        private readonly Func<long> clock;
        private readonly Action<Exception>? log;
        private Operation? active;
        private string? owner;
        private string epoch = "";
        private long nextRequest;
        private bool disposed;
        private sealed class Operation(Guid client, ClientScreenshot request, CancellationToken token) : IDisposable {
            internal readonly Guid Client = client;
            internal readonly ClientScreenshot Request = request;
            internal readonly CancellationTokenSource Lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            internal ScreenshotStatus? CancelReason;
            public void Dispose() => this.Lifetime.Dispose();
        }
        internal ScreenshotCoordinator(Func<ScreenshotQuality, CancellationToken, Task<ScreenshotImage>> capture, Action<Exception>? log = null, Func<long>? clock = null) {
            this.capture = capture; this.log = log; this.clock = clock ?? (() => Environment.TickCount64);
        }
        internal bool Busy { get { lock (this.gate) return this.active != null; } }
        internal void SetContext(string? owner, string epoch) {
            lock (this.gate) {
                if (this.owner == owner && this.epoch == epoch) return;
                this.owner = owner; this.epoch = epoch;
                if (this.active is { } operation) { operation.CancelReason = ScreenshotStatus.Stale; operation.Lifetime.Cancel(); }
            }
        }
        internal async Task<ScreenshotStatus> RequestAsync(Guid client, ClientScreenshot request, CancellationToken connectionToken, Func<ServerScreenshot, bool, bool> send) {
            if (!request.Valid || connectionToken.IsCancellationRequested) return ScreenshotStatus.Cancelled;
            Operation? operation = null;
            ScreenshotStatus? rejected = null;
            lock (this.gate) {
                if (request.Cancel) {
                    if (this.active is { } current && current.Client == client && current.Request.RequestId == request.RequestId &&
                        current.Request.OwnerKey == request.OwnerKey && current.Request.OwnerEpoch == request.OwnerEpoch) {
                        current.CancelReason = ScreenshotStatus.Cancelled; current.Lifetime.Cancel();
                    }
                    return ScreenshotStatus.Cancelled;
                }
                if (this.disposed) rejected = ScreenshotStatus.Unavailable;
                else if (this.owner == null) rejected = ScreenshotStatus.NotLoggedIn;
                else if (this.owner != request.OwnerKey || this.epoch != request.OwnerEpoch) rejected = ScreenshotStatus.Stale;
                else if (this.active != null || this.clock() < this.nextRequest) rejected = ScreenshotStatus.Busy;
                else {
                    operation = new Operation(client, request, connectionToken);
                    operation.Lifetime.CancelAfter(TimeSpan.FromSeconds(ScreenshotProtocol.TransferTimeoutSeconds));
                    this.active = operation; this.nextRequest = this.clock() + 2_000;
                }
            }
            if (rejected is { } failure) { Reply(failure); return failure; }
            var work = operation!;
            ScreenshotStatus result;
            try {
                if (!Reply(ScreenshotStatus.Capturing)) return ScreenshotStatus.Cancelled;
                using var captureTimeout = CancellationTokenSource.CreateLinkedTokenSource(work.Lifetime.Token);
                captureTimeout.CancelAfter(TimeSpan.FromSeconds(ScreenshotProtocol.CaptureTimeoutSeconds));
                var image = await this.capture(request.Quality, captureTimeout.Token).ConfigureAwait(false);
                work.Lifetime.Token.ThrowIfCancellationRequested();
                if (image.Bytes.Length > ScreenshotProtocol.MaxImageBytes) throw new IOException("Encoded image exceeds screenshot budget.");
                if (image.Width > ScreenshotProtocol.LongEdge(request.Quality) || image.Height > ScreenshotProtocol.LongEdge(request.Quality) ||
                    !ScreenshotProtocol.IsJpeg(image.Bytes, image.Width, image.Height)) throw new InvalidDataException("Capture produced an invalid image.");
                var digest = SHA256.HashData(image.Bytes);
                int chunks = (image.Bytes.Length + ScreenshotProtocol.ChunkBytes - 1) / ScreenshotProtocol.ChunkBytes;
                for (int index = 0; index < chunks; index++) {
                    work.Lifetime.Token.ThrowIfCancellationRequested();
                    int offset = index * ScreenshotProtocol.ChunkBytes;
                    var data = image.Bytes.AsSpan(offset, Math.Min(ScreenshotProtocol.ChunkBytes, image.Bytes.Length - offset)).ToArray();
                    var packet = new ServerScreenshot { RequestId = request.RequestId, OwnerKey = request.OwnerKey, OwnerEpoch = request.OwnerEpoch,
                        Status = ScreenshotStatus.Data, Width = image.Width, Height = image.Height, CapturedAtUnixMilliseconds = image.CapturedAtUnixMilliseconds,
                        TotalBytes = image.Bytes.Length, ChunkCount = chunks, ChunkIndex = index, Data = data, Digest = digest };
                    if (!packet.Valid || packet.Encode().Length > ScreenshotProtocol.MaxPacketBytes) throw new InvalidDataException("Capture packet exceeds protocol limits.");
                    // Never fill the normal outgoing queue with a whole image, including on a slow connection.
                    while (!send(packet, true)) await Task.Delay(15, work.Lifetime.Token).ConfigureAwait(false);
                    await Task.Delay(15, work.Lifetime.Token).ConfigureAwait(false);
                }
                return ScreenshotStatus.Data;
            } catch (OperationCanceledException) {
                lock (this.gate) result = work.CancelReason ?? (connectionToken.IsCancellationRequested ? ScreenshotStatus.Cancelled : ScreenshotStatus.TimedOut);
            } catch (InvalidDataException ex) { this.log?.Invoke(ex); result = ScreenshotStatus.Unavailable; }
            catch (IOException) { result = ScreenshotStatus.TooLarge; }
            catch (Exception ex) { this.log?.Invoke(ex); result = ScreenshotStatus.Unavailable; }
            finally {
                lock (this.gate) { if (ReferenceEquals(this.active, work)) this.active = null; work.Dispose(); }
            }
            if (!connectionToken.IsCancellationRequested) Reply(result);
            return result;

            bool Reply(ScreenshotStatus status) => !connectionToken.IsCancellationRequested && send(new ServerScreenshot {
                RequestId = request.RequestId, OwnerKey = request.OwnerKey, OwnerEpoch = request.OwnerEpoch, Status = status,
            }, false);
        }
        public void Dispose() {
            lock (this.gate) {
                this.disposed = true;
                if (this.active is { } operation) { operation.CancelReason = ScreenshotStatus.Cancelled; operation.Lifetime.Cancel(); }
            }
        }
    }
}
