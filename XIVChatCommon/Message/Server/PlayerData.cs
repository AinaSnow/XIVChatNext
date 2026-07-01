using MessagePack;

namespace XIVChatCommon.Message.Server {
    [MessagePackObject]
    public class PlayerData : Encodable {
        [Key(0)]
        public readonly string homeWorld;

        [Key(1)]
        public readonly string currentWorld;

        [Key(2)]
        public readonly string location;

        [Key(3)]
        public readonly string name;

        [Key(4)]
        public readonly uint? mapId;

        [Key(5)]
        public readonly float? mapX;

        [Key(6)]
        public readonly float? mapY;

        [Key(7)]
        public readonly string? mapFilenameId;

        [Key(8)]
        public readonly ushort? mapSizeFactor;

        public PlayerData(string homeWorld, string currentWorld, string location, string name, uint? mapId = null, float? mapX = null, float? mapY = null, string? mapFilenameId = null, ushort? mapSizeFactor = null) {
            this.homeWorld = homeWorld;
            this.currentWorld = currentWorld;
            this.location = location;
            this.name = name;
            this.mapId = mapId;
            this.mapX = mapX;
            this.mapY = mapY;
            this.mapFilenameId = mapFilenameId;
            this.mapSizeFactor = mapSizeFactor;
        }

        [IgnoreMember]
        protected override byte Code => (byte)ServerOperation.PlayerData;

        public static PlayerData Decode(byte[] bytes) {
            return MessagePackSerializer.Deserialize<PlayerData>(bytes);
        }

        protected override byte[] PayloadEncode() {
            return MessagePackSerializer.Serialize(this);
        }
    }
}
