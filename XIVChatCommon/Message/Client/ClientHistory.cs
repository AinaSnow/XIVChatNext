using MessagePack;

namespace XIVChatCommon.Message.Client {
    [MessagePackObject]
    public sealed class ClientHistory : Encodable {
        [Key(0)] public HistoryCursor? After { get; set; }
        [Key(1)] public long? Through { get; set; }
        [IgnoreMember] protected override byte Code => (byte)ClientOperation.History;
        protected override byte[] PayloadEncode() => MessagePackSerializer.Serialize(this);
        public static ClientHistory Decode(byte[] bytes) => MessagePackSerializer.Deserialize<ClientHistory>(bytes);
    }
}
