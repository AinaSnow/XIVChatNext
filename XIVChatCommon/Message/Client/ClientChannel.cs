using MessagePack;

namespace XIVChatCommon.Message.Client {
    [MessagePackObject]
    public class ClientChannel : Encodable {
        protected override byte Code => (byte) ClientOperation.Channel;

        [Key(0)]
        public InputChannel Channel { get; set; }

        [Key(1)] public string? RequestId { get; set; }
        [Key(2)] public string? ExpectedOwnerKey { get; set; }
        [Key(3)] public string? ExpectedOwnerEpoch { get; set; }

        public static ClientChannel Decode(byte[] bytes) {
            return MessagePackSerializer.Deserialize<ClientChannel>(bytes);
        }

        protected override byte[] PayloadEncode() {
            return MessagePackSerializer.Serialize(this);
        }
    }
}
