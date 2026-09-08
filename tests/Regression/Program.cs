using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Sodium;
using XIVChatCommon;
using XIVChatPlugin;
using XIVChat_Desktop;

var tests = new (string Name, Func<Task> Run)[] {
    ("Encrypted frames tolerate fragmented reads", FragmentedFrame),
    ("EOF at every frame boundary terminates", TruncatedFrames),
    ("Invalid frame lengths are rejected", InvalidLengths),
    ("Handshake cancellation interrupts a silent peer", HandshakeCancellation),
    ("TCP handshake derives matching directional keys", HandshakeRoundTrip),
    ("Disconnect closes TCP and permits repeated cleanup", DisconnectClosesTcp),
    ("Atomic saves rotate the previous valid configuration", BackupRotation),
    ("Corrupt primary recovers without altering original", RecoverCorruptPrimary),
    ("Missing primary recovers backup", RecoverMissingPrimary),
    ("Invalid primary and backup remain intact", BothInvalid),
    ("Failed validation preserves primary and backup", FailedSave),
    ("Interrupted temporary write does not affect loading", InterruptedSave),
    ("Appended protocol fields preserve old wire layouts", WorkbenchTests.ProtocolCompatibility),
    ("Cursor pages bound frames and report missing history", WorkbenchTests.CursorPages),
    ("SQLite deduplicates stable IDs and isolates characters", WorkbenchTests.Persistence),
    ("Retention protects notes, bookmarks and favorite sources", WorkbenchTests.Retention),
    ("Database recovery preserves corrupt and newer databases", WorkbenchTests.Recovery),
    ("Chinese search, literal queries and pagination", WorkbenchTests.Search),
    ("Failed database writes roll back and preserve checkpoints", WorkbenchTests.WriteFailure),
    ("100,000-row history remains searchable and paged", WorkbenchTests.LargeHistory),
};
int failures = 0;
foreach (var test in tests) {
    try {
        await test.Run().WaitAsync(TimeSpan.FromSeconds(60));
        Console.WriteLine($"PASS {test.Name}");
    } catch (Exception ex) {
        failures++;
        Console.WriteLine($"FAIL {test.Name}: {ex}");
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static void Assert(bool value, string message) {
    if (!value) throw new Exception(message);
}

static async Task Throws<T>(Func<Task> action) where T : Exception {
    try { await action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

static async Task<(byte[] Frame, byte[] Key)> MakeFrame() {
    var key = SecretBox.GenerateKey();
    using var stream = new MemoryStream();
    await SecretMessage.SendSecretMessage(stream, key, Encoding.UTF8.GetBytes("分片消息 / fragmented chat"));
    return (stream.ToArray(), key);
}

static async Task FragmentedFrame() {
    var (frame, key) = await MakeFrame();
    using var stream = new FragmentedStream(frame);
    var decoded = await SecretMessage.ReadSecretMessage(stream, key);
    Assert(Encoding.UTF8.GetString(decoded) == "分片消息 / fragmented chat", "Payload changed");
}

static async Task TruncatedFrames() {
    var (frame, key) = await MakeFrame();
    for (int length = 0; length < frame.Length; length++) {
        using var stream = new FragmentedStream(frame[..length]);
        await Throws<EndOfStreamException>(() => SecretMessage.ReadSecretMessage(stream, key));
        Assert(stream.ReadCount <= length + 2, "EOF repeatedly read");
    }
}

static async Task InvalidLengths() {
    foreach (uint length in new uint[] { 0, 15, 128001, uint.MaxValue }) {
        byte[] header = new byte[28];
        BitConverter.GetBytes(length).CopyTo(header, 0);
        using var stream = new MemoryStream(header);
        await Throws<ArgumentOutOfRangeException>(() => SecretMessage.ReadSecretMessage(stream, new byte[32]));
    }
}

static async Task HandshakeCancellation() {
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    using var client = new TcpClient();
    var accept = listener.AcceptTcpClientAsync();
    await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
    using var server = await accept;
    using var timeout = new CancellationTokenSource(100);
    await Throws<OperationCanceledException>(() => KeyExchange.ClientHandshake(PublicKeyBox.GenerateKeyPair(), client.GetStream(), timeout.Token));
}

static async Task HandshakeRoundTrip() {
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    using var client = new TcpClient();
    var accept = listener.AcceptTcpClientAsync();
    await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
    using var server = await accept;
    var serverTask = KeyExchange.ServerHandshake(PublicKeyBox.GenerateKeyPair(), server.GetStream());
    var clientResult = await KeyExchange.ClientHandshake(PublicKeyBox.GenerateKeyPair(), client.GetStream());
    var serverResult = await serverTask;
    Assert(clientResult.Keys.tx.SequenceEqual(serverResult.Keys.rx), "TX/RX mismatch");
    Assert(clientResult.Keys.rx.SequenceEqual(serverResult.Keys.tx), "RX/TX mismatch");
    client.Dispose();
    await Throws<EndOfStreamException>(() => SecretMessage.ReadSecretMessage(server.GetStream(), serverResult.Keys.rx));
}

static async Task DisconnectClosesTcp() {
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    using var peer = new TcpClient();
    var accept = listener.AcceptTcpClientAsync();
    await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
    using var wrapped = new TcpConnected(await accept);
    wrapped.Disconnect();
    wrapped.Disconnect();
    Assert(!wrapped.Connected, "Still marked connected");
    var read = await peer.GetStream().ReadAsync(new byte[1]);
    Assert(read == 0, "Peer did not observe EOF");
}

static SampleConfig Deserialize(string contents) {
    try {
        return JsonSerializer.Deserialize<SampleConfig>(contents) ?? throw new InvalidDataException("Null config");
    } catch (JsonException ex) { throw new InvalidDataException("Invalid config", ex); }
}

static void Save(string path, int version) => ConfigurationFile.Save(path,
    JsonSerializer.Serialize(new SampleConfig(version, "test key")), text => { _ = Deserialize(text); });

static Task WithFiles(Action<string> test) {
    var directory = Path.Combine(Path.GetTempPath(), "XIVChat-regression-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try { test(Path.Combine(directory, "config.json")); }
    finally {
        // Only delete files in this test's freshly created, unique directory.
        foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
    }
    return Task.CompletedTask;
}

static Task BackupRotation() => WithFiles(path => {
    Save(path, 1); Save(path, 2);
    Assert(Deserialize(File.ReadAllText(path)).Version == 2, "Latest save missing");
    Assert(Deserialize(File.ReadAllText(path + ".bak")).Version == 1, "Previous save missing");
    Assert(ConfigurationFile.Load(path, Deserialize, out var recovered)?.Version == 2 && !recovered, "Wrong primary loaded");
    Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(), "Temporary files leaked");
});

static Task RecoverCorruptPrimary() => WithFiles(path => {
    Save(path, 1); Save(path, 2);
    File.WriteAllText(path, "{truncated");
    var recovered = ConfigurationFile.Load(path, Deserialize, out var usedBackup);
    Assert(usedBackup && recovered?.Version == 1 && recovered.Key == "test key", "Backup recovery failed");
    Assert(File.ReadAllText(path) == "{truncated", "Load changed original");
    Save(path, recovered!.Version);
    Assert(Deserialize(File.ReadAllText(path + ".bak")).Version == 1, "Good backup overwritten");
    var archived = Directory.GetFiles(Path.GetDirectoryName(path)!, "config.json.corrupt-*");
    Assert(archived.Length == 1 && File.ReadAllText(archived[0]) == "{truncated", "Damaged original not archived");
});

static Task RecoverMissingPrimary() => WithFiles(path => {
    Save(path, 1); Save(path, 2); File.Delete(path);
    var recovered = ConfigurationFile.Load(path, Deserialize, out var usedBackup);
    Assert(usedBackup && recovered?.Version == 1, "Missing-primary fallback failed");
    Assert(!File.Exists(path), "Load unexpectedly wrote primary");
});

static Task BothInvalid() => WithFiles(path => {
    File.WriteAllText(path, "broken primary");
    File.WriteAllText(path + ".bak", "broken backup");
    try { ConfigurationFile.Load(path, Deserialize, out _); throw new Exception("Load unexpectedly succeeded"); }
    catch (IOException) { }
    Assert(File.ReadAllText(path) == "broken primary" && File.ReadAllText(path + ".bak") == "broken backup", "Failed recovery changed data");
});

static Task FailedSave() => WithFiles(path => {
    Save(path, 1); Save(path, 2);
    try { ConfigurationFile.Save(path, "invalid", text => { _ = Deserialize(text); }); throw new Exception("Save unexpectedly succeeded"); }
    catch (InvalidDataException) { }
    Assert(Deserialize(File.ReadAllText(path)).Version == 2 && Deserialize(File.ReadAllText(path + ".bak")).Version == 1, "Failed save changed files");
});

static Task InterruptedSave() => WithFiles(path => {
    Save(path, 1); File.WriteAllText(path + ".interrupted.tmp", "half-written");
    Assert(ConfigurationFile.Load(path, Deserialize, out var usedBackup)?.Version == 1 && !usedBackup, "Temporary file was treated as primary");
});

sealed record SampleConfig(int Version, string Key);

sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes) {
    public int ReadCount { get; private set; }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) {
        ReadCount++;
        return base.ReadAsync(buffer, offset, Math.Min(3, count), token);
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) {
        ReadCount++;
        return base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], token);
    }
}
