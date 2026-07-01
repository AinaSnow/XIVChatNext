using System.Collections.Generic;
using MessagePack;

namespace XIVChatCommon.Message.Relay {
    [MessagePackObject]
    public class RelayNewClient : IFromRelay {
        [Key(0)]
        public List<byte> PublicKey { get; set; } = new List<byte>();

        [Key(1)]
        public string Address { get; set; } = string.Empty;
    }
}