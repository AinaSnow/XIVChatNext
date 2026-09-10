using System;
using MessagePack;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;

namespace XIVChatCommon.Message {
    public enum PresenceState { Unknown, Online, Offline }

    [MessagePackObject]
    public sealed class ClientFriendPresence : Encodable {
        [Key(0)] public string RequestId { get; set; } = "";
        [Key(1)] public string OwnerKey { get; set; } = "";
        [Key(2)] public string OwnerEpoch { get; set; } = "";
        [Key(3)] public ulong ContentId { get; set; }
        [IgnoreMember] public bool Valid => RequestId?.Length is > 0 and <= 64 && OwnerKey?.Length is > 0 and <= 128 && OwnerEpoch?.Length is > 0 and <= 64 && ContentId != 0;
        [IgnoreMember] protected override byte Code => (byte)ClientOperation.FriendPresence;
        protected override byte[] PayloadEncode() => MessagePackSerializer.Serialize(this);
        public static ClientFriendPresence Decode(byte[] bytes) => MessagePackSerializer.Deserialize<ClientFriendPresence>(bytes);
    }

    [MessagePackObject]
    public sealed class ServerFriendPresence : Encodable {
        [Key(0)] public string RequestId { get; set; } = "";
        [Key(1)] public string OwnerKey { get; set; } = "";
        [Key(2)] public string OwnerEpoch { get; set; } = "";
        [Key(3)] public ulong ContentId { get; set; }
        [Key(4)] public PresenceState Presence { get; set; }
        [Key(5)] public DateTime CheckedAt { get; set; }
        [Key(6)] public FriendListStatus Status { get; set; }
        [IgnoreMember] protected override byte Code => (byte)ServerOperation.FriendPresence;
        protected override byte[] PayloadEncode() => MessagePackSerializer.Serialize(this);
        public static ServerFriendPresence Decode(byte[] bytes) => MessagePackSerializer.Deserialize<ServerFriendPresence>(bytes);
    }
}
