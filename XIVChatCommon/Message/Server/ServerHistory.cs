using MessagePack;

namespace XIVChatCommon.Message.Server {
    [MessagePackObject]
    public sealed class ServerHistory : Encodable {
        [Key(0)] public ServerMessage[] Messages { get; set; } = [];
        [Key(1)] public HistoryCursor Cursor { get; set; } = new();
        [Key(2)] public long Through { get; set; }
        [Key(3)] public bool HasMore { get; set; }
        [Key(4)] public bool HasGap { get; set; }
        [IgnoreMember] protected override byte Code => (byte)ServerOperation.History;
        protected override byte[] PayloadEncode() => MessagePackSerializer.Serialize(this);
        public static ServerHistory Decode(byte[] bytes) => MessagePackSerializer.Deserialize<ServerHistory>(bytes);
    }
}
