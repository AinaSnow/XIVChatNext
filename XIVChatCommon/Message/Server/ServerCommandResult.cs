using MessagePack;

namespace XIVChatCommon.Message.Server {
    public enum CommandFailure { NotLoggedIn, IdentityChanged, ChannelChanged, QueueFull, InvalidRequest, Disconnected, Unavailable }
    public enum CommandStage { Rejected, Queued, Submitted }

    [MessagePackObject]
    public sealed class ServerCommandResult : Encodable {
        [Key(0)] public string? RequestId { get; set; }
        [Key(1)] public CommandFailure Failure { get; set; }
        [Key(2)] public CommandStage Stage { get; set; }
        [Key(3)] public int SubmittedParts { get; set; }
        [IgnoreMember] protected override byte Code => (byte)ServerOperation.CommandResult;
        protected override byte[] PayloadEncode() => MessagePackSerializer.Serialize(this);
        public static ServerCommandResult Decode(byte[] bytes) => MessagePackSerializer.Deserialize<ServerCommandResult>(bytes);
    }
}
