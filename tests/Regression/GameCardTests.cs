using MessagePack;
using XIVChatCommon;
using XIVChatCommon.Message;

internal static class GameCardTests {
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static Task Identity() {
        Check(GameItemIdentity.Resolve(123, 500_000, true) == (123u, GameItemKind.Collectible), "Explicit collectible kind must override stale HQ flag");
        Check(GameItemIdentity.Resolve(2_000_123, 2_000_000, false) == (2_000_123u, GameItemKind.EventItem), "Key item ID was adjusted");
        Check(GameItemIdentity.Resolve(2_000_123, null, true) == (2_000_123u, GameItemKind.EventItem), "Legacy key item became HQ");
        Check(GameItemIdentity.Resolve(500_123, null, false) == (123u, GameItemKind.Collectible), "Legacy collectible became HQ");
        Check(GameItemIdentity.Resolve(1_000_123, null, false) == (123u, GameItemKind.Hq), "Legacy HQ failed");
        Check(GameItemIdentity.Resolve(123, 0, true) == (123u, GameItemKind.Normal), "Explicit normal kind ignored");
        return Task.CompletedTask;
    }
    public static Task AttributesAndWire() {
        var parameters = GameItemDetails.Merge(new[] {
            new ItemParameter { Id = 21, Name = "Defense", NqValue = 32 },
            new ItemParameter { Id = 24, Name = "Magic Defense", NqValue = 32 },
            new ItemParameter { Id = 12, Name = "Physical Damage", NqValue = 10 },
        }, new[] {
            new ItemParameter { Id = 12, Name = "different locale", HqDelta = 2 },
            new ItemParameter { Id = 21, HqDelta = 3 },
            new ItemParameter { Id = 24, HqDelta = 3 },
            new ItemParameter { Id = 6, Name = "Piety", HqDelta = -1 },
        });
        Check(parameters.Count == 4, "Equal-valued defense fields were lost");
        Check(parameters.Single(p => p.Id == 21).Value(true) == 35, "HQ value is a delta, not a second stat");
        Check(parameters.Single(p => p.Id == 24).Value(false) == 32, "NQ includes HQ delta");
        Check(parameters.Single(p => p.Id == 12).Value(true) == 12, "Weapon HQ bonus not merged by ID");
        Check(parameters.Single(p => p.Id == 6).Value(true) == -1, "Negative delta lost");
        var chunk = new TextChunk("test") { ItemDetails = new GameItemDetails { Parameters = parameters }, DataSource = new GameDataSource { Language = "English", Version = "test-version", RetrievedAtUnixMilliseconds = 42 } };
        var bytes = MessagePackSerializer.Serialize(chunk);
        var copy = MessagePackSerializer.Deserialize<TextChunk>(bytes);
        Check(copy.ItemDetails!.Parameters[0].Value(true) == 35 && copy.DataSource!.Version == "test-version", "Structured data/provenance lost in wire serialization");
        // A pre-upgrade sender has only fields 0..23; absent trailing fields stay null.
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(24);
        for (int i = 0; i < 24; i++) { if (i == 3) writer.Write(false); else if (i == 4) writer.Write("old"); else writer.WriteNil(); }
        writer.Flush();
        var old = MessagePackSerializer.Deserialize<TextChunk>(buffer.WrittenMemory);
        Check(old.ItemDetails == null && old.DataSource == null && old.Content == "old", "Old messages cannot be read");
        return Task.CompletedTask;
    }
}
