using System.Collections.Generic;
using MessagePack;

namespace XIVChatCommon.Message.Relay {
    [MessagePackObject]
    public class RelayClientDisconnect : IFromRelay, IToRelay {
        [Key(0)]
        public List<byte> PublicKey { get; set; } = new List<byte>();
    }
}