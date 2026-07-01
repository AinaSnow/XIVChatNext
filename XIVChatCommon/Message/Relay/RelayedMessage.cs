using System.Collections.Generic;
using MessagePack;

namespace XIVChatCommon.Message.Relay {
    [MessagePackObject]
    public class RelayedMessage : IFromRelay, IToRelay {
        [Key(0)]
        public List<byte> PublicKey { get; set; } = new List<byte>();

        [Key(1)]
        public List<byte> Message { get; set; } = new List<byte>();
    }
}