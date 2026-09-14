using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using XIVChat.Relay.Protocol;

namespace XIVChat.Relay;

public sealed class RelayHub(RelayStore store, IHostApplicationLifetime lifetime, IConfiguration configuration) : IDisposable {
    private readonly object gate = new();
    private readonly Dictionary<string, Host> hosts = new();
    private readonly Dictionary<string, Session> sessions = new();
    private readonly int maxSessions = Math.Clamp(configuration.GetValue("Relay:MaxSessions", 256), 1, 4096);
    private readonly int bytesPerSecond = Math.Clamp(configuration.GetValue("Relay:BytesPerSecond", 4 * 1024 * 1024), 65536, 64 * 1024 * 1024);
    private readonly CancellationToken stopping = lifetime.ApplicationStopping;
    private Task? monitor;
    private sealed class Host(string id, WebSocket socket) {
        public string Id { get; } = id;
        public WebSocket Socket { get; } = socket;
        public CancellationTokenSource Life { get; } = new();
        public SemaphoreSlim Writer { get; } = new(1);
        public bool Registered { get; set; }
    }
    private sealed class Session(Host host, string client, CancellationToken aborted, CancellationToken stopping) {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public Host Host { get; } = host;
        public string Client { get; } = client;
        public string Ticket { get; } = RelayStore.Secret();
        public bool Claimed { get; set; }
        public TaskCompletionSource<WebSocket> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Life { get; } = CancellationTokenSource.CreateLinkedTokenSource(host.Life.Token, aborted, stopping);
    }
    public void Start() => monitor = Monitor();
    public static string Bearer(HttpContext context) {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) && header.Length <= 135 ? header[7..] : "";
    }
    public bool Online(string device) { lock (gate) return hosts.TryGetValue(device, out var host) && host.Registered && !host.Life.IsCancellationRequested; }
    public object ManagementSnapshot() {
        lock (gate) return new {
            onlineDevices = hosts.Values.Where(h => h.Registered && !h.Life.IsCancellationRequested).Select(h => h.Id).ToArray(),
            connections = sessions.Values.Where(s => !s.Life.IsCancellationRequested).Select(s => new { id = s.Id, deviceId = s.Host.Id, clientId = s.Client, ready = s.Ready.Task.IsCompletedSuccessfully }).ToArray(),
            maxSessions, bytesPerSecond,
        };
    }
    public async Task HostControl(HttpContext context) {
        var identity = store.Host(Bearer(context));
        if (identity == null) { context.Response.StatusCode = 401; return; }
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        Host? host = null;
        try {
            var hello = await SocketIO.Read<HostHello>(socket, context.RequestAborted);
            if (hello.Version != RelayProtocol.Version || !store.Register(identity.Id, hello.Fingerprint)) return;
            host = new Host(identity.Id, socket);
            lock (gate) {
                if (hosts.Count >= 64 || hosts.ContainsKey(identity.Id)) return;
                hosts.Add(identity.Id, host);
            }
            using var stopped = stopping.Register(() => host.Life.Cancel());
            using var aborted = context.RequestAborted.Register(() => host.Life.Cancel());
            using var abortSocket = host.Life.Token.Register(socket.Abort);
            await SocketIO.Send(socket, new ControlMessage("registered"), host.Life.Token);
            lock (gate) host.Registered = true;
            // Keep a receive pending so WebSocket ping/pong detection works. No client-to-server control messages after Hello.
            var bytes = new byte[1];
            await socket.ReceiveAsync(bytes.AsMemory(), host.Life.Token);
        } catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or System.Text.Json.JsonException) { }
        finally {
            if (host != null) {
                lock (gate) { if (hosts.TryGetValue(host.Id, out var current) && ReferenceEquals(current, host)) hosts.Remove(host.Id); }
                host.Life.Cancel(); socket.Abort();
                Task[] pending; lock (gate) pending = sessions.Values.Where(s => s.Host == host).Select(s => s.Completed.Task).ToArray();
                await Task.WhenAll(pending);
                host.Life.Dispose(); host.Writer.Dispose();
            }
        }
    }
    public async Task Client(HttpContext context) {
        var identity = store.Client(Bearer(context));
        if (identity == null) { context.Response.StatusCode = 401; return; }
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        Session session;
        lock (gate) {
            if (!hosts.TryGetValue(identity.DeviceId, out var host) || !host.Registered || host.Life.IsCancellationRequested) { context.Response.StatusCode = 409; return; }
            if (sessions.Count >= maxSessions || sessions.Values.Count(s => s.Host == host) >= 32 || sessions.Values.Any(s => s.Client == identity.Id)) { context.Response.StatusCode = 429; return; }
            session = new Session(host, identity.Id, context.RequestAborted, stopping); sessions.Add(session.Id, session);
        }
        WebSocket? client = null; WebSocket? remote = null;
        try {
            client = await context.WebSockets.AcceptWebSocketAsync();
            using var abortClient = session.Life.Token.Register(client.Abort);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(session.Life.Token)) {
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                await session.Host.Writer.WaitAsync(deadline.Token);
                try { await SocketIO.Send(session.Host.Socket, new ControlMessage("session", session.Id, session.Ticket), deadline.Token); }
                finally { session.Host.Writer.Release(); }
                remote = await session.Ready.Task.WaitAsync(deadline.Token);
                await SocketIO.Send(remote, new ControlMessage("ready", session.Id), deadline.Token);
                await SocketIO.Send(client, new ControlMessage("ready", session.Id), deadline.Token);
            }
            using var abortRemote = session.Life.Token.Register(remote.Abort);
            var forward = SocketIO.Pump(client, remote, bytesPerSecond, session.Life.Token);
            var backward = SocketIO.Pump(remote, client, bytesPerSecond, session.Life.Token);
            await Task.WhenAny(forward, backward);
            session.Life.Cancel();
            try { await Task.WhenAll(forward, backward); } catch (Exception) { }
        } catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or TimeoutException) { }
        finally {
            lock (gate) sessions.Remove(session.Id);
            session.Life.Cancel();
            client?.Abort(); client?.Dispose(); remote?.Abort();
            if (session.Ready.Task.IsCompletedSuccessfully) session.Ready.Task.Result.Abort();
            session.Completed.TrySetResult(); session.Life.Dispose();
        }
    }
    public async Task HostData(HttpContext context, string id) {
        var identity = store.Host(Bearer(context));
        if (identity == null) { context.Response.StatusCode = 401; return; }
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        Session session;
        lock (gate) {
            var ticket = context.Request.Headers["X-Relay-Ticket"].ToString();
            if (!sessions.TryGetValue(id, out session!) || session.Host.Id != identity.Id || session.Claimed || session.Life.IsCancellationRequested ||
                !hosts.TryGetValue(identity.Id, out var host) || host != session.Host ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(ticket), Encoding.UTF8.GetBytes(session.Ticket))) { context.Response.StatusCode = 403; return; }
            session.Claimed = true;
        }
        try {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            if (!session.Ready.TrySetResult(socket)) return;
            await session.Completed.Task.WaitAsync(context.RequestAborted);
        } finally { lock (gate) { if (sessions.ContainsKey(id)) session.Life.Cancel(); } }
    }
    private async Task Monitor() {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try {
            while (await timer.WaitForNextTickAsync(stopping)) {
                lock (gate) {
                    foreach (var host in hosts.Values) if (!store.ActiveDevice(host.Id)) host.Life.Cancel();
                    foreach (var session in sessions.Values) if (!store.ActiveClient(session.Client)) session.Life.Cancel();
                }
            }
        } catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }
    public void Dispose() { lock (gate) { foreach (var host in hosts.Values) host.Life.Cancel(); foreach (var session in sessions.Values) session.Life.Cancel(); } }
}
