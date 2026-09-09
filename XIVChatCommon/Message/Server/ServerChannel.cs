using MessagePack;

namespace XIVChatCommon.Message.Server {
    [MessagePackObject]
    public class ServerChannel : Encodable {
        [Key(0)]
        public readonly byte channel;

        [Key(1)]
        public readonly string name;

        [Key(2)] public long Revision { get; set; }

        [IgnoreMember]
        public InputChannel InputChannel => (InputChannel)this.channel;

        protected override byte Code => (byte)ServerOperation.Channel;

        public ServerChannel(InputChannel channel, string name) : this((byte)channel, name) { }

        public ServerChannel(byte channel, string name) {
            this.channel = channel;
            this.name = name;
        }

        public static ServerChannel Decode(byte[] bytes) {
            return MessagePackSerializer.Deserialize<ServerChannel>(bytes);
        }

        protected override byte[] PayloadEncode() {
            return MessagePackSerializer.Serialize(this);
        }
    }
}
