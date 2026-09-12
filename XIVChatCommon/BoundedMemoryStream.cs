using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XIVChatCommon {
    /// <summary>A seekable encoder destination that rejects growth before allocating it.</summary>
    public sealed class BoundedMemoryStream : Stream {
        private readonly MemoryStream inner = new();
        private readonly int limit;
        public BoundedMemoryStream(int limit) { if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit)); this.limit = limit; }
        public byte[] ToArray() => this.inner.ToArray();
        private void Check(long end) { if (end < 0 || end > this.limit) throw new IOException("Screenshot exceeds its encoded byte budget."); }
        public override bool CanRead => this.inner.CanRead;
        public override bool CanSeek => this.inner.CanSeek;
        public override bool CanWrite => this.inner.CanWrite;
        public override long Length => this.inner.Length;
        public override long Position { get => this.inner.Position; set { this.Check(value); this.inner.Position = value; } }
        public override void Flush() => this.inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => this.inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) {
            long position = checked((origin switch { SeekOrigin.Begin => 0, SeekOrigin.Current => this.Position, SeekOrigin.End => this.Length, _ => throw new ArgumentOutOfRangeException(nameof(origin)) }) + offset);
            this.Check(position); return this.inner.Seek(position, SeekOrigin.Begin);
        }
        public override void SetLength(long value) { this.Check(value); this.inner.SetLength(value); }
        public override void Write(byte[] buffer, int offset, int count) { this.Check(checked(this.Position + count)); this.inner.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { this.Check(checked(this.Position + buffer.Length)); this.inner.Write(buffer); }
        public override void WriteByte(byte value) { this.Check(this.Position + 1); this.inner.WriteByte(value); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested(); this.Write(buffer, offset, count); return Task.CompletedTask;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested(); this.Write(buffer.Span); return ValueTask.CompletedTask;
        }
        protected override void Dispose(bool disposing) { if (disposing) this.inner.Dispose(); base.Dispose(disposing); }
    }
}
