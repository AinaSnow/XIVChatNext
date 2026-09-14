using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using XIVChat.Relay;
using XIVChat.Relay.Protocol;
using XIVChat.Relay.Transport;
using XIVChatCommon;
using Sodium;

var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "src", "XIVChat.Relay", "XIVChat.Relay.csproj"))) root = Directory.GetParent(root)?.FullName ?? throw new Exception("Run inside the repository.");
var data = Path.Combine(root, "artifacts", "relay-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(data);
using var store = new RelayStore(Path.Combine(data, "relay.sqlite3"));
var first = store.AddDevice("first"); var second = store.AddDevice("second");
using var certA = RelayTls.CreateCertificate(); using var certB = RelayTls.CreateCertificate();
var keys = PublicKeyBox.GenerateKeyPair();
var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
var address = "http://127.0.0.1:" + port;
using var all = new CancellationTokenSource(TimeSpan.FromMinutes(3));
var token = all.Token;
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
Process? service = null;
var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
var count = 0;
void Check(bool success, string description) { if (!success) throw new Exception(description); Console.WriteLine($"PASS {++count}: {description}"); }
async Task Eventually(Func<Task<bool>> condition, string message) {
    for (var i = 0; i < 100; i++) { token.ThrowIfCancellationRequested(); if (await condition()) return; await Task.Delay(100, token); }
    throw new Exception(message);
}
async Task StartService() {
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(Path.Combine(root, "src", "XIVChat.Relay", "bin", "Debug", "net10.0", "XIVChat.Relay.dll"));
    start.ArgumentList.Add("--urls"); start.ArgumentList.Add(address);
    start.Environment["XIVCHAT_RELAY_DATA"] = data;
    start.Environment["Logging__LogLevel__Default"] = "Warning";
    service = Process.Start(start)!;
    service.OutputDataReceived += (_, e) => { if (e.Data != null && output.Count < 30) output.Enqueue(e.Data); };
    service.ErrorDataReceived += (_, e) => { if (e.Data != null && output.Count < 30) output.Enqueue(e.Data); };
    service.BeginOutputReadLine(); service.BeginErrorReadLine();
    await Eventually(async () => { try { return (await http.GetAsync(address + "/healthz", token)).IsSuccessStatusCode; } catch { return false; } }, "Relay failed to start: " + string.Join("\n", output));
}
async Task Echo(Stream stream, byte marker, CancellationToken stop) {
    // Actual legacy key exchange and encrypted application frames are carried inside endpoint TLS.
    var magic = new byte[3]; await stream.ReadExactlyAsync(magic, stop);
    if (!magic.SequenceEqual(new byte[] { 14, 20, 67 })) throw new IOException("Bad magic.");
    var handshake = await KeyExchange.ServerHandshake(keys, stream, stop);
    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, new byte[] { marker }, stop);
    while (!stop.IsCancellationRequested) {
        var packet = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, stop);
        await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, packet, stop);
    }
}
async Task<(Stream Stream, SessionKeys Keys)> Connect(PairResult pair, byte marker) {
    var stream = await RelayEndpoint.ConnectAsync(address, pair.Credential, pair.Fingerprint, token);
    await stream.WriteAsync(new byte[] { 14, 20, 67 }, token);
    var handshake = await KeyExchange.ClientHandshake(PublicKeyBox.GenerateKeyPair(), stream, token);
    if (!(await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, token)).SequenceEqual(new[] { marker })) throw new Exception("Wrong game endpoint.");
    return (stream, handshake.Keys);
}
async Task Roundtrip(Stream stream, SessionKeys sessionKeys, byte[] packet) {
    await SecretMessage.SendSecretMessage(stream, sessionKeys.tx, packet, token);
    if (!(await SecretMessage.ReadSecretMessage(stream, sessionKeys.rx, token)).SequenceEqual(packet)) throw new Exception("Corrupted application data.");
}
async Task<bool> Rejected(Func<Task> action) { try { await action(); return false; } catch { return true; } }
var hostA = new RelayHostEndpoint(address, first.Credential, certA);
var hostB = new RelayHostEndpoint(address, second.Credential, certB);
var aOnline = false; var bOnline = false;
hostA.StateChanged += state => aOnline = state == "connected";
hostB.StateChanged += state => bOnline = state == "connected";
hostA.SessionFailed += message => output.Enqueue("Host A: " + message);
hostB.SessionFailed += message => output.Enqueue("Host B: " + message);
Task? aTask = null, bTask = null;
try {
    Check(await Rejected(() => { RelayProtocol.BaseUri("http://example.com"); return Task.CompletedTask; }), "non-loopback plaintext relay URLs rejected");
    Check(await Rejected(() => { RelayProtocol.BaseUri("https://user:secret@example.com"); return Task.CompletedTask; }), "credentials in relay URLs rejected");
    if (OperatingSystem.IsWindows()) Check(WindowsSecret.Unprotect(WindowsSecret.Protect("fixture-secret")) == "fixture-secret", "endpoint credentials roundtrip through Windows DPAPI");
    await StartService();
    aTask = hostA.RunAsync((stream, stop) => Echo(stream, 1, stop), token);
    bTask = hostB.RunAsync((stream, stop) => Echo(stream, 2, stop), token);
    await Eventually(() => Task.FromResult(aOnline && bOnline), "Hosts failed to register.");
    Check(true, "two plugin endpoints register on a real service process");
    var inviteA = await RelayEndpoint.InviteAsync(address, first.Credential, RelayTls.Fingerprint(certA), token);
    Check(RelayEndpoint.DecodeInvitation(RelayEndpoint.EncodeInvitation(inviteA)) == inviteA, "invitation import preserves address, expiry and fingerprint");
    var pairA = await RelayEndpoint.PairAsync(inviteA, "client A", token);
    Check(await Rejected(async () => { await RelayEndpoint.PairAsync(inviteA, "reused", token); }), "one-time invitation cannot be reused");
    Check(await Rejected(async () => { using var rejected = await RelayEndpoint.ConnectAsync(address, RelayStore.Secret(), pairA.Fingerprint, token); }), "unknown client credential rejected");
    var wrongPinRejected = false;
    try { using var rejected = await RelayEndpoint.ConnectAsync(address, pairA.Credential, RelayTls.Fingerprint(certB), token); }
    catch (System.Security.Authentication.AuthenticationException) { wrongPinRejected = true; }
    Check(wrongPinRejected, "wrong endpoint TLS fingerprint fails certificate authentication");
    await Task.Delay(200, token);
    var connectionA = await Connect(pairA, 1);
    using var streamA = connectionA.Stream;
    await Roundtrip(streamA, connectionA.Keys, Encoding.UTF8.GetBytes("中文聊天 / XIVChat relay"));
    Check(true, "real end-to-end TLS plus existing encrypted chat handshake and bidirectional Unicode data");
    Check(await Rejected(async () => { using var rejected = await RelayEndpoint.ConnectAsync(address, pairA.Credential, pairA.Fingerprint, token); }), "same client cannot attach a duplicate active session");
    var inviteB = await RelayEndpoint.InviteAsync(address, second.Credential, RelayTls.Fingerprint(certB), token);
    var pairB = await RelayEndpoint.PairAsync(inviteB, "client B", token);
    var connectionB = await Connect(pairB, 2);
    using var streamB = connectionB.Stream;
    await Task.WhenAll(Roundtrip(streamA, connectionA.Keys, new byte[] { 1, 2, 3 }), Roundtrip(streamB, connectionB.Keys, new byte[] { 7, 8, 9 }));
    Check(true, "simultaneous game endpoints do not mix data");
    var packet = RandomNumberGenerator.GetBytes(120_000);
    for (var i = 0; i < 20; i++) await Roundtrip(streamA, connectionA.Keys, packet);
    await Roundtrip(streamB, connectionB.Keys, new byte[] { 42 });
    Check(true, "2.4 MB screenshot-sized chunks roundtrip while another session stays responsive");
    await RawRelayChecks.Run(address, store, token, Check);
    store.Revoke("client", pairA.ClientId);
    using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token)) {
        stop.CancelAfter(TimeSpan.FromSeconds(6));
        var closed = false;
        try { closed = await streamA.ReadAsync(new byte[1], stop.Token) == 0; }
        catch (Exception ex) when (ex is IOException or WebSocketException) { closed = true; }
        Check(closed, "revoking a client closes the live session before the test deadline");
    }
    Check(await Rejected(async () => { using var rejected = await RelayEndpoint.ConnectAsync(address, pairA.Credential, pairA.Fingerprint, token); }), "revoked client cannot reconnect");
    await Roundtrip(streamB, connectionB.Keys, new byte[] { 99 });
    Check(true, "revocation leaves unrelated sessions running");
    service!.Kill(true); await service.WaitForExitAsync(token); service.Dispose(); service = null;
    await Eventually(() => Task.FromResult(!bOnline), "Host did not notice service termination.");
    await StartService();
    await Eventually(() => Task.FromResult(aOnline && bOnline), "Hosts did not reconnect.");
    var reconnected = await Connect(pairB, 2); using var afterRestart = reconnected.Stream;
    await Roundtrip(afterRestart, reconnected.Keys, new byte[] { 4, 5, 6 });
    Check(true, "service restart preserves pairing and hosts reconnect with fresh TLS sessions");
    store.Revoke("device", second.Id);
    await Eventually(() => Task.FromResult(!bOnline), "Revoked plugin did not disconnect.");
    Check(store.Client(pairB.Credential) == null, "revoking a game device revokes access through all its client credentials");
    using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(data, "relay.sqlite3"))) {
        db.Open(); using var command = db.CreateCommand(); command.CommandText = "SELECT token_hash FROM devices WHERE id=$id";
        command.Parameters.AddWithValue("$id", first.Id);
        Check((string)command.ExecuteScalar()! == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first.Credential))), "relay persists only the registration credential digest");
    }
    Console.WriteLine($"All {count} relay checks passed.");
} catch { foreach (var line in output) Console.Error.WriteLine(line); throw; }
finally {
    all.Cancel();
    try { await Task.WhenAll(new[] { aTask, bTask }.OfType<Task>()); } catch (OperationCanceledException) { }
    if (service != null) { if (!service.HasExited) service.Kill(true); service.Dispose(); }
}
