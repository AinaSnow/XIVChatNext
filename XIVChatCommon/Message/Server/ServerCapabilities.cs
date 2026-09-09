using MessagePack;

namespace XIVChatCommon.Message.Server {
    [MessagePackObject]
    public sealed class ServerCapabilities : Encodable {
        [Key(0)] public string ServiceId { get; set; } = "";
        [Key(1)] public string RunId { get; set; } = "";
        [Key(2)] public bool StableMessageIds { get; set; } = true;
        [Key(3)] public bool CursorBacklog { get; set; }
        [Key(4)] public bool FriendSnapshots { get; set; }
        [Key(5)] public bool ChannelSubscriptions { get; set; }
        [Key(6)] public bool GuardedCommands { get; set; }
        [IgnoreMember] protected override byte Code => (byte)ServerOperation.Capabilities;
        protected override byte[] PayloadEncode() => MessagePackSerializer.Serialize(this);
        public static ServerCapabilities Decode(byte[] bytes) => MessagePackSerializer.Deserialize<ServerCapabilities>(bytes);
    }
}
