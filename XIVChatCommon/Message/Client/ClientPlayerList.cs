using MessagePack;

namespace XIVChatCommon.Message.Client {
    [MessagePackObject]
    public class ClientPlayerList : Encodable {
        [Key(0)]
        public PlayerListType Type { get; set; }

        [Key(1)] public string? RequestId { get; set; }
        [Key(2)] public string? ExpectedOwnerKey { get; set; }
        [Key(3)] public string? ExpectedOwnerEpoch { get; set; }

        protected override byte Code => (byte) ClientOperation.PlayerList;

        public static ClientPlayerList Decode(byte[] bytes) {
            return MessagePackSerializer.Deserialize<ClientPlayerList>(bytes);
        }

        protected override byte[] PayloadEncode() {
            return MessagePackSerializer.Serialize(this);
        }
    }
}
