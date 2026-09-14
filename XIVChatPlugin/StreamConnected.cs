using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace XIVChatPlugin;

internal sealed class StreamConnected(Stream stream) : BaseClient {
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Closed => closed.Task;
    internal override bool Connected { get; set; } = true;
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => stream.ReadAsync(buffer, offset, count, token);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => stream.ReadAsync(buffer, token);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) => stream.WriteAsync(buffer, offset, count, token);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) => stream.WriteAsync(buffer, token);
    public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);
    public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);
    public override void Flush() => stream.Flush();
    public override Task FlushAsync(CancellationToken token) => stream.FlushAsync(token);
    protected override void Dispose(bool disposing) { if (disposing) { try { stream.Dispose(); } finally { closed.TrySetResult(); } } base.Dispose(disposing); }
    public override bool CanRead => stream.CanRead;
    public override bool CanWrite => stream.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
