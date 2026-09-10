using System;
using MessagePack;

namespace XIVChatCommon.Message.Server {
    public enum GameEventKind : byte {
        DutyReady = 1,
        Login = 2,
        Logout = 3,
        TerritoryChanged = 4,
        ConnectionLost = 5,
        NotificationTest = 6,
    }

    [MessagePackObject]
    public sealed class ServerGameEvent : Encodable {
        public const int MaxPacketBytes = 4096;
        [Key(0)] public string EventId { get; set; } = "";
        [Key(1)] public string ServiceId { get; set; } = "";
        [Key(2)] public string RunId { get; set; } = "";
        [Key(3)] public CharacterIdentity? Owner { get; set; }
        [Key(4)] public string OwnerEpoch { get; set; } = "";
        [Key(5)] public GameEventKind Kind { get; set; }
        [Key(6)] public DateTime Timestamp { get; set; }
        [Key(7)] public DateTime? ExpiresAt { get; set; }
        [Key(8)] public uint DataId { get; set; }
        [Key(9)] public string Name { get; set; } = "";

        [IgnoreMember] protected override byte Code => (byte)ServerOperation.GameEvent;
        protected override byte[] PayloadEncode() => MessagePackSerializer.Serialize(this);
        public static ServerGameEvent Decode(byte[] bytes) => MessagePackSerializer.Deserialize<ServerGameEvent>(bytes);

        public bool IsValid(bool fromPlugin = false) =>
            EventId is { Length: > 0 and <= 192 } && ServiceId is { Length: > 0 and <= 64 } &&
            RunId is { Length: > 0 and <= 64 } && OwnerEpoch is { Length: > 0 and <= 64 } &&
            Name is { Length: <= 256 } && Timestamp.Year is >= 2000 and <= 2200 &&
            Enum.IsDefined(typeof(GameEventKind), Kind) &&
            (Owner == null ? !fromPlugin : Owner.IsComplete && Owner.Name.Length <= 80 && Owner.HomeWorld is { Length: <= 80 }) &&
            (!fromPlugin || Kind is >= GameEventKind.DutyReady and <= GameEventKind.TerritoryChanged) &&
            (Kind != GameEventKind.DutyReady || ExpiresAt is { } expiry && expiry > Timestamp && expiry <= Timestamp.AddMinutes(1));
    }
}
