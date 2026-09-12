using MessagePack;
using System.Security.Cryptography;
using XIVChatCommon;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;
using XIVChatPlugin;

internal static class ScreenshotTests {
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Malformed screenshot was accepted"); }
    private static ClientScreenshot Request(string id = "shot") => new() { RequestId = id, OwnerKey = "cid:1", OwnerEpoch = "login1" };
    // Header fixture only, for protocol and coordinator tests; native decode tests generate an actual JPEG.
    private static ScreenshotImage HeaderFixture(int length = 3 * ScreenshotProtocol.ChunkBytes + 23) {
        var bytes = new byte[length]; new Random(17).NextBytes(bytes);
        byte[] header = [0xff, 0xd8, 0xff, 0xc0, 0, 17, 8, 0x02, 0xd0, 0x05, 0, 3, 1, 0x11, 0, 2, 0x11, 0, 3, 0x11, 0,
            0xff, 0xda, 0, 12, 3, 1, 0, 2, 0, 3, 0, 0, 63, 0];
        header.CopyTo(bytes, 0); bytes[^2] = 0xff; bytes[^1] = 0xd9;
        return new(bytes, 1280, 720, 123456789);
    }
    private static ServerScreenshot[] Packets(ScreenshotImage image) {
        int count = (image.Bytes.Length + ScreenshotProtocol.ChunkBytes - 1) / ScreenshotProtocol.ChunkBytes;
        var digest = SHA256.HashData(image.Bytes);
        return Enumerable.Range(0, count).Select(i => new ServerScreenshot { RequestId = "shot", OwnerKey = "cid:1", OwnerEpoch = "login1",
            Status = ScreenshotStatus.Data, Width = image.Width, Height = image.Height, CapturedAtUnixMilliseconds = image.CapturedAtUnixMilliseconds,
            TotalBytes = image.Bytes.Length, ChunkIndex = i, ChunkCount = count, Digest = digest,
            Data = image.Bytes.Skip(i * ScreenshotProtocol.ChunkBytes).Take(ScreenshotProtocol.ChunkBytes).ToArray() }).ToArray();
    }
    private static ServerScreenshot Copy(ServerScreenshot packet) => ServerScreenshot.Decode(packet.Encode()[1..]);
    internal static Task Protocol() {
        var request = Request(); Assert(request.Valid, "Normal request invalid");
        Assert(ClientScreenshot.Decode(request.Encode()[1..]).Valid && request.Encode()[0] == 13, "Request wire mismatch");
        request.OwnerEpoch = ""; Assert(!request.Valid, "Missing epoch accepted"); request = Request();
        request.Quality = (ScreenshotQuality)99; Assert(!request.Valid, "Unknown quality accepted"); request = Request();
        request.RequestId = new string('x', 65); Assert(!request.Valid, "Unbounded request ID");
        var oldCapabilities = MessagePackSerializer.Deserialize<ServerCapabilities>(MessagePackSerializer.Serialize(new object[] {
            "service", "run", true, false, false, false, false, false, false, false, true }));
        Assert(oldCapabilities.GameCards && !oldCapabilities.Screenshots, "Old server unexpectedly supports screenshots");
        var legacyFields = MessagePackSerializer.Deserialize<object[]>(MessagePackSerializer.Serialize(new ServerCapabilities { GameCards = true, Screenshots = true }));
        Assert(legacyFields.Length == 12 && (bool)legacyFields[10] && (bool)legacyFields[11], "Capability fields moved");
        Assert((int)ClientPreference.GameCardsSupport == 6 && (int)ClientPreference.ScreenshotSupport == 7 && (byte)ServerOperation.GameCard == 16, "Existing operation/preferences changed");
        foreach (var packet in Packets(HeaderFixture(ScreenshotProtocol.MaxImageBytes))) {
            Assert(packet.Valid && packet.Encode().Length <= ScreenshotProtocol.MaxPacketBytes && packet.Encode()[0] == 17, "Oversized or invalid maximum-image packet");
        }
        var malformed = Packets(HeaderFixture())[0];
        malformed.TotalBytes = int.MaxValue; Assert(!malformed.Valid, "Unbounded allocation accepted");
        malformed = Packets(HeaderFixture())[0]; malformed.ChunkIndex = -1; Assert(!malformed.Valid, "Negative chunk index accepted");
        malformed = Packets(HeaderFixture())[0]; malformed.Width = 9000; Assert(!malformed.Valid, "Oversized dimensions accepted");
        malformed = Packets(HeaderFixture())[0]; malformed.Format = "image/png"; Assert(!malformed.Valid, "Unsupported format accepted");
        malformed = Packets(HeaderFixture())[0]; malformed.Data = []; Assert(!malformed.Valid, "Missing data accepted");
        malformed = Packets(HeaderFixture())[0]; malformed.Status = ScreenshotStatus.Busy; Assert(!malformed.Valid, "Failure carrying bytes accepted");
        return Task.CompletedTask;
    }
    internal static Task Assembly() {
        var image = HeaderFixture(); var packets = Packets(image); var assembly = new ScreenshotAssembler("shot", "cid:1", "login1");
        Assert(assembly.Add(packets[2]) == null && assembly.Add(packets[2]) == null && assembly.ReceivedBytes == packets[2].Data.Length, "Duplicate increased byte count");
        Assert(assembly.Add(packets[0]) == null && assembly.Add(packets[3]) == null, "Incomplete image published");
        var completed = assembly.Add(packets[1]);
        Assert(completed != null && completed.Bytes.SequenceEqual(image.Bytes) && completed.Width == 1280, "Out-of-order image corrupt");
        foreach (Action<ServerScreenshot> change in new Action<ServerScreenshot>[] {
            p => p.OwnerEpoch = "other", p => p.OwnerKey = "cid:2", p => p.RequestId = "old", p => p.Width = 1279,
            p => p.CapturedAtUnixMilliseconds++, p => p.Digest[0] ^= 1, p => p.TotalBytes--,
        }) {
            var target = new ScreenshotAssembler("shot", "cid:1", "login1"); target.Add(packets[0]); var changed = Copy(packets[1]); change(changed);
            Reject(() => target.Add(changed));
        }
        var duplicate = new ScreenshotAssembler("shot", "cid:1", "login1"); duplicate.Add(packets[0]);
        var conflict = Copy(packets[0]); conflict.Data[70] ^= 1; Reject(() => duplicate.Add(conflict));
        var corrupt = Packets(image); corrupt[1].Data[100] ^= 1;
        var corruptAssembly = new ScreenshotAssembler("shot", "cid:1", "login1");
        Reject(() => { foreach (var part in corrupt) corruptAssembly.Add(part); });
        var badHeader = HeaderFixture(); badHeader.Bytes[9] = 0x7f;
        var bad = new ScreenshotAssembler("shot", "cid:1", "login1"); Reject(() => { foreach (var part in Packets(badHeader)) bad.Add(part); });
        Assert(!ScreenshotProtocol.IsJpeg([0xff, 0xd8, 0xff, 0xe0, 0xff, 0xff, 0xff, 0xd9], 1, 1), "Truncated segment accepted");
        return Task.CompletedTask;
    }
    internal static async Task Budgets() {
        using var stream = new BoundedMemoryStream(100);
        stream.Write(new byte[99]); Assert(stream.Length == 99, "Encoder stream length");
        try { await stream.WriteAsync(new byte[2]); throw new Exception("Oversize async write succeeded"); } catch (IOException) { }
        Assert(stream.Length == 99, "Oversize write altered data");
        stream.WriteByte(1); Assert(stream.Length == 100, "Exact bound rejected");
        try { stream.Seek(101, SeekOrigin.Begin); throw new Exception("Oversize seek succeeded"); } catch (IOException) { }
        try { stream.SetLength(101); throw new Exception("Oversize length succeeded"); } catch (IOException) { }
        var queue = new BoundedByteQueue(8, 800);
        Assert(queue.TryWriteWithin(new byte[100], 2, 200) && queue.TryWriteWithin(new byte[100], 2, 200), "Bulk budget unavailable");
        using (var sending = await queue.ReadAsync()) {
            Assert(!queue.TryWriteWithin(new byte[1], 2, 200), "Active bulk write excluded from budget");
            Assert(queue.TryWrite(new byte[100]), "Bulk blocked interactive packet");
        }
        Assert(!queue.TryWriteWithin(new byte[100], 2, 200), "Queued chat ignored by bulk budget");
        queue.Close(); Assert(queue.Usage == (0, 0), "Queue retained cancelled data");
    }
    internal static async Task Coordinator() {
        long now = 1000; var pending = new TaskCompletionSource<ScreenshotImage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new ScreenshotCoordinator((_, token) => pending.Task.WaitAsync(token), clock: () => now);
        coordinator.SetContext("cid:1", "login1"); var client = Guid.NewGuid(); var replies = new List<ServerScreenshot>();
        bool Send(ServerScreenshot packet, bool bulk) { replies.Add(packet); return true; }
        var first = coordinator.RequestAsync(client, Request(), CancellationToken.None, Send);
        Assert(coordinator.Busy && replies.Single().Status == ScreenshotStatus.Capturing, "Capture not reserved");
        Assert(await coordinator.RequestAsync(Guid.NewGuid(), Request("other"), CancellationToken.None, Send) == ScreenshotStatus.Busy, "Concurrent peer started a second capture");
        var cancel = Request(); cancel.Cancel = true;
        await coordinator.RequestAsync(Guid.NewGuid(), cancel, CancellationToken.None, Send); Assert(coordinator.Busy, "Other peer cancelled capture");
        pending.SetResult(HeaderFixture()); Assert(await first == ScreenshotStatus.Data && !coordinator.Busy, "Capture did not finish");
        Assert(replies.Count(p => p.Status == ScreenshotStatus.Data) == 4, "Chunk count wrong");
        Assert(await coordinator.RequestAsync(client, Request("fast"), CancellationToken.None, Send) == ScreenshotStatus.Busy, "Rate limit missing");
        now += 3000; pending = new(TaskCreationOptions.RunContinuationsAsynchronously); replies.Clear();
        var switched = coordinator.RequestAsync(client, Request(), CancellationToken.None, Send);
        coordinator.SetContext("cid:2", "login2"); Assert(await switched == ScreenshotStatus.Stale && !replies.Any(p => p.Status == ScreenshotStatus.Data), "Old role image leaked");
        Assert(await coordinator.RequestAsync(client, Request(), CancellationToken.None, Send) == ScreenshotStatus.Stale, "Wrong owner accepted");
        coordinator.SetContext(null, "login3"); Assert(await coordinator.RequestAsync(client, Request(), CancellationToken.None, Send) == ScreenshotStatus.NotLoggedIn, "Capture after logout accepted");
        now += 3000; coordinator.SetContext("cid:1", "login1"); pending = new(TaskCreationOptions.RunContinuationsAsynchronously); replies.Clear();
        var cancelled = coordinator.RequestAsync(client, Request(), CancellationToken.None, Send);
        await coordinator.RequestAsync(client, cancel, CancellationToken.None, Send);
        Assert(await cancelled == ScreenshotStatus.Cancelled && !coordinator.Busy, "Explicit cancel retained request");
        now += 3000; pending = new(TaskCreationOptions.RunContinuationsAsynchronously); replies.Clear(); using var disconnected = new CancellationTokenSource();
        var abandoned = coordinator.RequestAsync(client, Request(), disconnected.Token, Send); disconnected.Cancel();
        Assert(await abandoned == ScreenshotStatus.Cancelled && !coordinator.Busy && replies.Count == 1, "Disconnected client received more data");
    }
    internal static async Task Backpressure() {
        int attempts = 0; var packets = new List<ServerScreenshot>();
        using var coordinator = new ScreenshotCoordinator((_, _) => Task.FromResult(HeaderFixture()), clock: () => 0);
        coordinator.SetContext("cid:1", "login1");
        var result = await coordinator.RequestAsync(Guid.NewGuid(), Request(), CancellationToken.None, (packet, bulk) => {
            if (bulk && ++attempts <= 5) return false; packets.Add(packet); return true;
        });
        Assert(result == ScreenshotStatus.Data && attempts == 9 && packets.Count == 5, "Slow queue lost or duplicated chunks");
        using var timeout = new ScreenshotCoordinator((_, _) => Task.FromException<ScreenshotImage>(new OperationCanceledException()), clock: () => 0);
        timeout.SetContext("cid:1", "login1");
        Assert(await timeout.RequestAsync(Guid.NewGuid(), Request(), CancellationToken.None, (_, _) => true) == ScreenshotStatus.TimedOut && !timeout.Busy, "Capture timeout not released");
        using var oversized = new ScreenshotCoordinator((_, _) => Task.FromResult(HeaderFixture(ScreenshotProtocol.MaxImageBytes + 1)), clock: () => 0);
        oversized.SetContext("cid:1", "login1");
        Assert(await oversized.RequestAsync(Guid.NewGuid(), Request(), CancellationToken.None, (_, _) => true) == ScreenshotStatus.TooLarge, "Oversized encoded capture escaped budget");
    }
}
