using System.Net.WebSockets;
using System.Text.Json;
using XIVChat.Relay.Protocol;

namespace XIVChat.Relay.Transport;

/// <summary>One reader and one writer, no unbounded background buffering. Each write is sent immediately (TLS handshakes need no Flush).</summary>
public sealed class WebSocketStream(WebSocket socket) : Stream {
    private readonly SemaphoreSlim writer = new(1);
    private int disposed;
    private int frameBytes;
    private CancellationTokenSource? frameDeadline;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) {
        if (buffer.Length == 0) return 0;
        while (true) {
            using var read = frameDeadline == null ? null : CancellationTokenSource.CreateLinkedTokenSource(token, frameDeadline.Token);
            var result = await socket.ReceiveAsync(buffer, read?.Token ?? token);
            if (result.MessageType == WebSocketMessageType.Close) return 0;
            frameBytes += result.Count;
            if (result.MessageType != WebSocketMessageType.Binary || frameBytes > RelayProtocol.MaxDataBytes)
                throw new IOException("Invalid relay data frame.");
            if (result.EndOfMessage) { frameBytes = 0; frameDeadline?.Dispose(); frameDeadline = null; }
            else if (frameDeadline == null) { frameDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)); }
            if (result.Count != 0) return result.Count;
        }
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await writer.WaitAsync(deadline.Token);
        try {
            while (buffer.Length != 0) {
                var count = Math.Min(16 * 1024, buffer.Length);
                await socket.SendAsync(buffer[..count], WebSocketMessageType.Binary, true, deadline.Token);
                buffer = buffer[count..];
            }
        } finally { writer.Release(); }
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) => WriteAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    protected override void Dispose(bool disposing) {
        if (disposing && Interlocked.Exchange(ref disposed, 1) == 0) { socket.Abort(); socket.Dispose(); frameDeadline?.Dispose(); }
        base.Dispose(disposing);
    }
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken token) => Task.CompletedTask;
    public override bool CanRead => disposed == 0;
    public override bool CanWrite => disposed == 0;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

public static class RelayWire {
    public static async Task SendAsync<T>(WebSocket socket, T value, CancellationToken token) {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, RelayProtocol.Json);
        if (bytes.Length > RelayProtocol.MaxControlBytes) throw new IOException("Relay control message is too large.");
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Binary, true, token);
    }
    public static async Task<T> ReadAsync<T>(WebSocket socket, CancellationToken token) {
        var buffer = new byte[RelayProtocol.MaxControlBytes]; var used = 0; var fragments = 0;
        using var partial = CancellationTokenSource.CreateLinkedTokenSource(token);
        do {
            var result = await socket.ReceiveAsync(buffer.AsMemory(used), partial.Token);
            used += result.Count;
            if (result.MessageType != WebSocketMessageType.Binary || ++fragments > 128) throw new IOException("Relay control connection closed or sent an invalid message.");
            if (result.EndOfMessage) return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, used), RelayProtocol.Json) ?? throw new IOException("Empty relay response.");
            if (fragments == 1) partial.CancelAfter(TimeSpan.FromSeconds(15));
        } while (used < buffer.Length);
        throw new IOException("Relay control message exceeds the limit.");
    }
}
