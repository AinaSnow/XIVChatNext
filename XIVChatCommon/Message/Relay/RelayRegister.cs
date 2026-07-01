using MessagePack;

namespace XIVChatCommon.Message.Relay {
    [MessagePackObject]
    public class RelayRegister : IToRelay {
        [Key(0)]
        public string AuthToken { get; set; } = string.Empty;

        [Key(1)]
        public byte[] PublicKey { get; set; } = System.Array.Empty<byte>();
    }
}