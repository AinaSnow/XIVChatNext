using MessagePack;

namespace XIVChatCommon.Message.Server {
    [MessagePackObject]
    public class ServerPlayerList : Encodable {
        [Key(0)]
        public PlayerListType Type { get; set; }

        [Key(1)]
        public Player[] Players { get; set; }

        [Key(2)] public string? RequestId { get; set; }
        [Key(3)] public CharacterIdentity? Owner { get; set; }
        [Key(4)] public string? OwnerEpoch { get; set; }
        [Key(5)] public string? SnapshotId { get; set; }
        [Key(6)] public System.DateTime CapturedAt { get; set; }
        [Key(7)] public int PageIndex { get; set; }
        [Key(8)] public int PageCount { get; set; } = 1;
        [Key(9)] public FriendListStatus Status { get; set; }

        protected override byte Code => (byte) ServerOperation.PlayerList;

        public ServerPlayerList(PlayerListType type, Player[] players) {
            this.Type = type;
            this.Players = players;
        }

        public static ServerPlayerList Decode(byte[] bytes) {
            return MessagePackSerializer.Deserialize<ServerPlayerList>(bytes);
        }

        protected override byte[] PayloadEncode() {
            return MessagePackSerializer.Serialize(this);
        }
    }
}
