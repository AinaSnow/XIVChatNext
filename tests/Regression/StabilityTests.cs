using System.Net;
using System.Net.Sockets;
using System.Text;
using MessagePack;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;
using XIVChatPlugin;

internal static class StabilityTests {
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    public static async Task OutgoingBudget() {
        var queue = new BoundedByteQueue(2, 10);
        Check(queue.TryWrite(new byte[6]) && queue.TryWrite(new byte[4]), "Exact budgets rejected");
        using var active = await queue.ReadAsync();
        Check(!queue.TryWrite(new byte[1]), "An active send escaped the queue budget");
        active.Dispose(); active.Dispose();
        Check(queue.Usage == (1, 4L) && queue.TryWrite(new byte[6]), "Releasing a send did not free its budget exactly once");
        using var second = await queue.ReadAsync();
        queue.Close();
        Check(queue.Usage == (1, 4L) && !queue.TryWrite(new byte[1]), "Close retained queued packets or accepted more work");
        second.Dispose(); Check(queue.Usage == (0, 0L), "Active send leaked after close");
        var concurrent = new BoundedByteQueue(32, 320);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => {
            for (var i = 0; i < 1000; i++) concurrent.TryWrite(new byte[10]);
        })));
        Check(concurrent.Usage == (32, 320L), "Concurrent producers exceeded a budget"); concurrent.Close();

        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var peer = new TcpClient(); var accept = listener.AcceptTcpClientAsync();
        await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using var client = new TcpConnected(await accept);
        string? reason = null; client.OnFailure = r => reason = r;
        for (int i = 0; i < 512; i++) Check(client.SendEncoded(new byte[1]), "Ordinary packet rejected");
        Check(!client.SendEncoded(new byte[1]) && client.TokenSource.IsCancellationRequested && reason != null, "Overflow did not disconnect slow peer");
        Check(client.Queue.Usage == (0, 0L), "Disconnected peer retained its backlog");
        Check(await peer.GetStream().ReadAsync(new byte[1]) == 0, "Peer did not receive EOF on overflow");
    }

    public static Task GameCommands() {
        var queue = new GameCommandQueue(); var client = Guid.NewGuid(); var other = Guid.NewGuid();
        var context = new GameCommandContext("cid:1", "login1", 8);
        using var cancel = new CancellationTokenSource();
        GameCommand Make(Guid id, string text = "hello") => new(id, "request", context, text, null, cancel.Token);
        var batch = Enumerable.Range(0, 32).Select(_ => Make(client)).ToArray();
        Check(queue.TryEnqueue(batch) && !queue.TryEnqueue(new[] { Make(client) }), "Per-device command budget failed");
        Check(queue.TryEnqueue(new[] { Make(other) }), "One busy device blocked other devices");
        var oldHead = queue.Peek()!; queue.CancelClient(client);
        Check(queue.Take(oldHead) == null && queue.Usage.Count == 1, "Cancelled head removed another device's work");
        var command = queue.Peek()!;
        Check(GameCommandQueue.Validate(command, context) == null, "Valid command rejected");
        Check(GameCommandQueue.Validate(command, context with { Epoch = "login2" }) == CommandFailure.IdentityChanged, "Same-character relog allowed old work");
        Check(GameCommandQueue.Validate(command, context with { OwnerKey = "cid:2" }) == CommandFailure.IdentityChanged, "Character change allowed old work");
        Check(GameCommandQueue.Validate(command, context with { OwnerKey = null }) == CommandFailure.NotLoggedIn, "Logout allowed work");
        Check(GameCommandQueue.Validate(command, context with { ChannelRevision = 9 }) == CommandFailure.ChannelChanged, "Changed target allowed old plain text");
        Check(GameCommandQueue.Validate(command with { Channel = InputChannel.Party, Text = null }, context with { ChannelRevision = 9 }) == null, "Channel selection incorrectly required an unchanged channel");
        cancel.Cancel(); Check(GameCommandQueue.Validate(command, context) == CommandFailure.Disconnected, "Disconnected work remained valid");
        queue.Clear(); Check(queue.Usage == (0, 0), "Queue clear retained accounting");
        var oversized = Make(other, new string('x', GameCommandQueue.MaxBytes + 1)) with { Cancellation = default };
        Check(!queue.TryEnqueue(new[] { oversized }) && queue.Usage.Count == 0, "Byte-overflow batch partially entered queue");
        for (int i = 0; i < 4; i++) Check(queue.TryEnqueue(Enumerable.Range(0, 32).Select(_ => Make(Guid.Empty) with { ClientId = new Guid(i, 0, 0, new byte[8]), Cancellation = default }).ToArray()), "Total budget filled too early");
        Check(!queue.TryEnqueue(new[] { Make(Guid.NewGuid()) with { Cancellation = default } }), "Total command count exceeded");

        Check(GameCommandQueue.ValidateRequest(context, true, "r", "cid:1", "login1", 8, true) == null, "Guarded request rejected");
        Check(GameCommandQueue.ValidateRequest(context, true, "r", "cid:1", "login0", 8, true) == CommandFailure.IdentityChanged, "Delayed network request survived relog");
        Check(GameCommandQueue.ValidateRequest(context, true, "r", "cid:1", "login1", null, true) == CommandFailure.ChannelChanged, "Guarded input omitted target revision");
        Check(GameCommandQueue.ValidateRequest(context, false, null, null, null, null, true) == null, "Legacy commands lost compatibility");
        return Task.CompletedTask;
    }

    public static Task Backlog() {
        var backlog = new MessageBacklog("service", "run"); backlog.Configure(true, 3, 1_000_000);
        ServerMessage Message() => new(DateTime.UtcNow, ChatType.Say, Array.Empty<byte>(), Array.Empty<byte>(), new List<Chunk> { new TextChunk("hello") });
        for (var i = 0; i < 5; i++) backlog.Record(Message());
        Check(backlog.Snapshot().Messages.Select(m => m.Sequence).SequenceEqual(new long[] { 3, 4, 5 }), "Count retention did not keep newest records");
        backlog.Configure(true, 1, 1_000_000); Check(backlog.Usage.Count == 1, "Capacity reduction waited for another chat");
        backlog.Configure(false, 3, 1_000_000); Check(backlog.Usage == (0, 0L) && !backlog.Enabled, "Disabling history did not immediately clear it");
        backlog.Record(Message()); Check(backlog.Snapshot().Latest == 6 && backlog.Usage.Count == 0, "Disabled history stored data or lost sequence continuity");
        var page = HistoryPager.Create(backlog.Snapshot().Messages, "service", "run", 6,
            new ClientHistory { After = new HistoryCursor { ServiceId = "service", RunId = "run", Sequence = 5 } });
        Check(page.HasGap, "Cleared history concealed the catch-up gap");
        backlog.Configure(true, 10, 400);
        for (var i = 0; i < 10; i++) backlog.Record(Message());
        Check(backlog.Usage.Bytes <= 400 && backlog.Usage.Count < 10, "Byte budget did not trim history");
        return Task.CompletedTask;
    }

    public static Task SubscriptionsAndWire() {
        var union = ChannelSubscription.Union(false, new ushort[] { 10, 11 }, new ushort[] { 11, 12 });
        Check(union!.SequenceEqual(new ushort[] { 10, 11, 12 }), "View / notification union lost channels");
        Check(ChannelSubscription.Union(true, Array.Empty<ushort>(), Array.Empty<ushort>()) == null, "History did not retain all-channel subscription");
        var subscription = new ChannelSubscription(union);
        Check(subscription.Allows((ushort)(10 | 5 << 7 | 6 << 11)) && !subscription.Allows(13), "Raw source flags broke channel filtering");
        Check(!new ChannelSubscription(Array.Empty<ushort>()).Allows(10) && ChannelSubscription.All.Allows(127), "None / all subscriptions confused");
        foreach (var invalid in new[] { new ushort[129], new ushort[] { 128 } }) {
            try { _ = new ChannelSubscription(invalid); throw new Exception("Invalid subscription accepted"); } catch (ArgumentException) { }
        }
        var preferences = new ClientPreferences { Channels = union };
        Check(ClientPreferences.Decode(preferences.Encode()[1..]).Channels!.SequenceEqual(union!), "Typed subscription wire changed");
        var oldPrefs = MessagePackSerializer.Serialize(new object[] { new Dictionary<int, object> { [3] = true } });
        Check(ClientPreferences.Decode(oldPrefs).Channels == null, "Old peer no longer defaults to all channels");
        var oldMessage = ClientMessage.Decode(MessagePackSerializer.Serialize(new object[] { "old" }));
        Check(oldMessage.Content == "old" && oldMessage.ExpectedOwnerKey == null, "Old input layout changed");
        var modern = new ClientMessage("new") { RequestId = "r", ExpectedOwnerKey = "cid:1", ExpectedOwnerEpoch = "e", ExpectedChannelRevision = 7 };
        Check(ClientMessage.Decode(modern.Encode()[1..]).ExpectedChannelRevision == 7, "Guard fields did not round-trip");
        Check(!ServerCapabilities.Decode(MessagePackSerializer.Serialize(new object[] { "s", "r", true, true, true })).GuardedCommands, "Old capabilities enabled a new operation");
        var channel = new ServerChannel(InputChannel.Tell, "target") { Revision = 9 };
        Check(ServerChannel.Decode(channel.Encode()[1..]).Revision == 9, "Target revision did not round-trip");
        return Task.CompletedTask;
    }

    public static Task CacheAndSplit() {
        var cache = new BoundedCache<(int Language, uint Id, uint Kind), string?>(2); var loads = 0;
        string? Load((int Language, uint Id, uint Kind) key) { loads++; return key.Id == 0 ? null : $"{key}"; }
        cache.Get((0, 0, 0), Load); cache.Get((0, 0, 0), Load);
        Check(loads == 1 && cache.Hits == 1, "Missing rows were fetched repeatedly");
        var normal = cache.Get((0, 1, 0), Load); var hq = cache.Get((0, 1, 1_000_000), Load);
        Check(normal != hq && cache.Count == 2, "Item variants shared a cached value or cache grew without bound");
        cache.Get((1, 1, 0), Load); Check(loads == 4, "Language change reused other-language data");
        cache.Clear(); Check(cache.Count == 0, "Data-version reset retained cached values");
        var body = string.Concat(Enumerable.Repeat("中文🙂 hello ", 150));
        var parts = ChatTextSplitter.Split(body);
        Check(parts.All(p => Encoding.UTF8.GetByteCount(p) <= 500) && string.Concat(parts) == body, "UTF-8 splitting broke a character or exceeded the game limit");
        const string prefix = "/tell First Last@World ";
        parts = ChatTextSplitter.Split(prefix + body);
        Check(parts.All(p => p.StartsWith(prefix) && Encoding.UTF8.GetByteCount(p) <= 500) && string.Concat(parts.Select(p => p[prefix.Length..])) == body, "Split tell lost its fixed target or content");
        return Task.CompletedTask;
    }

    public static async Task RelayBackpressure() {
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new RelayConnected(new byte[32], null, async (_, token) => await sent.Task.WaitAsync(token));
        client.Write(new byte[] { 1, 2, 3 }, 0, 3);
        var flush = client.FlushAsync(default);
        Check(!flush.IsCompleted, "Relay released a packet before the websocket acknowledged sending it");
        sent.SetResult(); await flush;
        client.Receive(new byte[] { 1 }); client.Receive(new byte[] { 2, 3 });
        var read = new byte[3]; await client.ReadExactlyAsync(read);
        Check(read.SequenceEqual(new byte[] { 1, 2, 3 }), "Fragmented relay bytes changed");
        using var cancellation = new CancellationTokenSource(30);
        try { await client.ReadExactlyAsync(new byte[1], cancellation.Token); throw new Exception("Silent relay read did not cancel"); }
        catch (OperationCanceledException) { }
        for (var i = 0; i < 20 && !client.TokenSource.IsCancellationRequested; i++) client.Receive(new byte[128_000]);
        Check(client.TokenSource.IsCancellationRequested, "Relay input byte budget did not disconnect an overflowing peer");
    }
}
