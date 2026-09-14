using System.Net.WebSockets;
using System.Text.Json;
using XIVChat.Relay.Protocol;

namespace XIVChat.Relay;

internal static class SocketIO {
    internal static async Task Send<T>(WebSocket socket, T message, CancellationToken token) {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, RelayProtocol.Json);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Binary, true, deadline.Token);
    }
    internal static async Task<T> Read<T>(WebSocket socket, CancellationToken token) {
        var bytes = new byte[RelayProtocol.MaxControlBytes]; var used = 0; var fragments = 0;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        do {
            var result = await socket.ReceiveAsync(bytes.AsMemory(used), deadline.Token); used += result.Count;
            if (result.MessageType != WebSocketMessageType.Binary || ++fragments > 128) throw new IOException("Invalid control frame.");
            if (result.EndOfMessage) return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, used), RelayProtocol.Json) ?? throw new IOException("Empty frame.");
        } while (used < bytes.Length);
        throw new IOException("Oversized control frame.");
    }
    internal static async Task Pump(WebSocket from, WebSocket to, int bytesPerSecond, CancellationToken token) {
        var bytes = new byte[RelayProtocol.MaxDataBytes]; var budget = (double)bytesPerSecond * 2; var last = Environment.TickCount64;
        while (!token.IsCancellationRequested) {
            var used = 0; var fragments = 0;
            using var partial = CancellationTokenSource.CreateLinkedTokenSource(token);
            while (true) {
                var result = await from.ReceiveAsync(bytes.AsMemory(used), partial.Token);
                if (result.MessageType == WebSocketMessageType.Close) return;
                used += result.Count;
                if (result.MessageType != WebSocketMessageType.Binary || ++fragments > 256) throw new IOException("Invalid data frame.");
                if (result.EndOfMessage) break;
                if (used == bytes.Length) throw new IOException("Oversized data frame.");
                if (fragments == 1) partial.CancelAfter(TimeSpan.FromSeconds(15));
            }
            var now = Environment.TickCount64;
            budget = Math.Min((double)bytesPerSecond * 2, budget + (now - last) / 1000.0 * bytesPerSecond) - Math.Max(used, 64); last = now;
            if (budget < 0) throw new IOException("Relay rate limit exceeded.");
            using var write = CancellationTokenSource.CreateLinkedTokenSource(token); write.CancelAfter(TimeSpan.FromSeconds(10));
            await to.SendAsync(bytes.AsMemory(0, used), WebSocketMessageType.Binary, true, write.Token);
        }
    }
}
