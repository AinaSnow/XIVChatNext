using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using XIVChat.Relay.Protocol;

namespace XIVChat.Relay.Transport;

public static class RelayEndpoint {
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    public static string EncodeInvitation(Invitation invitation) => "xivchat-relay:" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(invitation, RelayProtocol.Json));
    public static Invitation DecodeInvitation(string value) {
        value = value.Trim();
        if (!value.StartsWith("xivchat-relay:", StringComparison.Ordinal) || value.Length > 8192) throw new ArgumentException("Invalid relay invitation.");
        var invite = JsonSerializer.Deserialize<Invitation>(Convert.FromBase64String(value[14..]), RelayProtocol.Json) ?? throw new ArgumentException("Invalid relay invitation.");
        RelayProtocol.BaseUri(invite.Server);
        if (invite.Version != RelayProtocol.Version || !RelayProtocol.ValidFingerprint(invite.Fingerprint) || invite.Code.Length is < 32 or > 128 || invite.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Relay invitation expired or is incompatible.");
        return invite;
    }
    public static async Task<Invitation> InviteAsync(string server, string hostCredential, string fingerprint, CancellationToken token) {
        using var request = new HttpRequestMessage(HttpMethod.Post, RelayProtocol.Endpoint(server, "v1/invitations"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostCredential);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        var invitation = await ReadResponse<Invitation>(response, token);
        if (!string.Equals(invitation.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)) throw new IOException("Relay registered a different endpoint fingerprint. Reconnect the plugin.");
        return invitation with { Server = server };
    }
    public static async Task<PairResult> PairAsync(Invitation invitation, string name, CancellationToken token) {
        if (invitation.ExpiresAt <= DateTimeOffset.UtcNow) throw new IOException("Relay invitation expired.");
        using var request = new HttpRequestMessage(HttpMethod.Post, RelayProtocol.Endpoint(invitation.Server, "v1/pair")) {
            Content = JsonContent.Create(new PairRequest(invitation.Code, name), options: RelayProtocol.Json),
        };
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        var result = await ReadResponse<PairResult>(response, token);
        if (!string.Equals(result.Fingerprint, invitation.Fingerprint, StringComparison.OrdinalIgnoreCase) || result.Credential.Length is < 32 or > 128)
            throw new IOException("Relay pairing returned a different endpoint fingerprint or invalid credential.");
        return result;
    }
    private static async Task<T> ReadResponse<T>(HttpResponseMessage response, CancellationToken token) {
        if (!response.IsSuccessStatusCode) throw new IOException($"Relay request failed ({(int)response.StatusCode}). Check authorization, invitation expiry and plugin connection.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        var bytes = new byte[RelayProtocol.MaxControlBytes + 1]; var count = 0;
        while (count < bytes.Length) { var read = await stream.ReadAsync(bytes.AsMemory(count), deadline.Token); if (read == 0) break; count += read; }
        if (count > RelayProtocol.MaxControlBytes) throw new IOException("Relay response exceeds the limit.");
        return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, count), RelayProtocol.Json) ?? throw new IOException("Invalid relay response.");
    }
    public static ClientWebSocket CreateSocket(string credential) {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + credential);
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);
        return socket;
    }
    public static async Task<Stream> ConnectAsync(string server, string credential, string fingerprint, CancellationToken token) {
        var socket = CreateSocket(credential);
        try {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(45));
            await socket.ConnectAsync(RelayProtocol.Endpoint(server, "v1/client", true), deadline.Token);
            var ready = await RelayWire.ReadAsync<ControlMessage>(socket, deadline.Token);
            if (ready.Kind != "ready") throw new IOException("Relay session is not ready.");
            return await RelayTls.ConnectAsync(new WebSocketStream(socket), fingerprint, deadline.Token);
        } catch { socket.Dispose(); throw; }
    }
}

/// <summary>Dedicated control connection; a separate TLS-over-WebSocket stream for every approved client session.</summary>
public sealed class RelayHostEndpoint(string server, string credential, X509Certificate2 certificate) {
    public event Action<string>? StateChanged;
    public event Action<string>? SessionFailed;
    public async Task RunAsync(Func<Stream, CancellationToken, Task> session, CancellationToken token) {
        var failures = 0;
        while (!token.IsCancellationRequested) {
            using var connected = CancellationTokenSource.CreateLinkedTokenSource(token);
            var tasks = new ConcurrentDictionary<string, Task>();
            try {
                StateChanged?.Invoke("connecting");
                using var socket = RelayEndpoint.CreateSocket(credential);
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    deadline.CancelAfter(TimeSpan.FromSeconds(20));
                    await socket.ConnectAsync(RelayProtocol.Endpoint(server, "v1/host", true), deadline.Token);
                    await RelayWire.SendAsync(socket, new HostHello(RelayProtocol.Version, RelayTls.Fingerprint(certificate)), deadline.Token);
                    if ((await RelayWire.ReadAsync<ControlMessage>(socket, deadline.Token)).Kind != "registered") throw new IOException("Relay registration failed.");
                }
                StateChanged?.Invoke("connected");
                var started = DateTime.UtcNow;
                while (!token.IsCancellationRequested) {
                    var offer = await RelayWire.ReadAsync<ControlMessage>(socket, connected.Token);
                    if (offer.Kind != "session" || offer.SessionId?.Length != 32 || offer.Ticket?.Length is not (>= 32 and <= 128)) throw new IOException("Invalid relay session offer.");
                    if (tasks.Count >= 32 || tasks.ContainsKey(offer.SessionId)) throw new IOException("Relay session limit exceeded.");
                    var task = Serve(offer, session, connected.Token);
                    tasks[offer.SessionId] = task;
                    _ = task.ContinueWith(_ => tasks.TryRemove(offer.SessionId, out var removed), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    if (DateTime.UtcNow - started > TimeSpan.FromMinutes(1)) failures = 0;
                }
            } catch (Exception) when (!token.IsCancellationRequested) { StateChanged?.Invoke("disconnected"); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { connected.Cancel(); await Task.WhenAll(tasks.Values); }
            if (token.IsCancellationRequested) break;
            var seconds = Math.Min(30, 1 << Math.Min(5, failures++));
            await Task.Delay(TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(Random.Shared.Next(500)), token);
        }
    }
    private async Task Serve(ControlMessage offer, Func<Stream, CancellationToken, Task> session, CancellationToken token) {
        try {
            using var socket = RelayEndpoint.CreateSocket(credential);
            socket.Options.SetRequestHeader("X-Relay-Ticket", offer.Ticket);
            Stream tls;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                await socket.ConnectAsync(RelayProtocol.Endpoint(server, "v1/host-data/" + offer.SessionId, true), deadline.Token);
                if ((await RelayWire.ReadAsync<ControlMessage>(socket, deadline.Token)).Kind != "ready") throw new IOException("Relay session was not accepted.");
                tls = await RelayTls.AcceptAsync(new WebSocketStream(socket), certificate, deadline.Token);
            }
            using (tls) using (token.Register(() => tls.Dispose())) await session(tls, token);
        } catch (Exception ex) when (!token.IsCancellationRequested) { SessionFailed?.Invoke(ex.GetType().Name + ": " + ex.Message); }
        catch (Exception) when (token.IsCancellationRequested) { }
    }
}
