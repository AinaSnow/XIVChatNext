using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace XIVChatCommon {
    /// <summary>A nonblocking producer queue whose budgets include the packet currently being sent.</summary>
    public sealed class BoundedByteQueue {
        private readonly object gate = new();
        private readonly Channel<byte[]> channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions {
            AllowSynchronousContinuations = false,
        });
        private readonly int maxCount;
        private readonly long maxBytes;
        private int count;
        private long bytes;
        private bool closed;

        public BoundedByteQueue(int maxCount, long maxBytes) {
            if (maxCount < 1 || maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxCount));
            this.maxCount = maxCount; this.maxBytes = maxBytes;
        }

        public (int Count, long Bytes) Usage { get { lock (this.gate) return (this.count, this.bytes); } }

        // Ownership is shared: callers must never mutate a buffer after it has been queued.
        public bool TryWrite(byte[] packet) => this.TryWriteWithin(packet, this.maxCount, this.maxBytes);

        // Bulk producers use a smaller shared-queue ceiling, leaving capacity for interactive traffic.
        public bool TryWriteWithin(byte[] packet, int countLimit, long byteLimit) {
            lock (this.gate) {
                if (this.closed || this.count >= Math.Min(countLimit, this.maxCount) || packet.LongLength > Math.Min(byteLimit, this.maxBytes) - this.bytes) return false;
                this.count++; this.bytes += packet.Length;
                return this.channel.Writer.TryWrite(packet);
            }
        }

        public async ValueTask<Lease> ReadAsync(CancellationToken token = default) =>
            new(this, await this.channel.Reader.ReadAsync(token));

        private void Release(byte[] packet) {
            lock (this.gate) { this.count--; this.bytes -= packet.Length; }
        }

        public void Close() {
            lock (this.gate) {
                if (this.closed) return;
                this.closed = true;
                this.channel.Writer.TryComplete();
                while (this.channel.Reader.TryRead(out var packet)) { this.count--; this.bytes -= packet.Length; }
            }
        }

        public sealed class Lease : IDisposable {
            private BoundedByteQueue? owner;
            public byte[] Bytes { get; }
            internal Lease(BoundedByteQueue owner, byte[] bytes) { this.owner = owner; this.Bytes = bytes; }
            public void Dispose() => Interlocked.Exchange(ref this.owner, null)?.Release(this.Bytes);
        }
    }
}
