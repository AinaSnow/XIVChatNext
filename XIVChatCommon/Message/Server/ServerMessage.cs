using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MessagePack;

namespace XIVChatCommon.Message.Server {
    [MessagePackObject]
    public class ServerMessage : Encodable {
        [MessagePackFormatter(typeof(MillisecondsDateTimeFormatter))]
        [Key(0)]
        public DateTime Timestamp { get; set; }

        [Key(1)]
        public ChatType Channel { get; set; }

        [Key(2)]
        public byte[] Sender { get; set; }

        [Key(3)]
        public byte[] Content { get; set; }

        [Key(4)]
        public List<Chunk> Chunks { get; set; }

        [Key(5)] public string? MessageId { get; set; }
        [Key(6)] public string? ServiceId { get; set; }
        [Key(7)] public string? RunId { get; set; }
        [Key(8)] public long Sequence { get; set; }
        [Key(9)] public CharacterIdentity? Owner { get; set; }
        [Key(10)] public CharacterIdentity? TellPeer { get; set; }
        [IgnoreMember] public string? LocalSource { get; set; }
        [IgnoreMember] public string? LocalStorageId { get; set; }

        [IgnoreMember]
        public string ContentText => XivString.GetText(this.Content);

        [IgnoreMember]
        public string SenderText => XivString.GetText(this.Sender);

        [IgnoreMember]
        protected override byte Code => (byte) ServerOperation.Message;

        public ServerMessage(DateTime timestamp, ChatType channel, byte[] sender, byte[] content, List<Chunk> chunks) {
            this.Timestamp = timestamp;
            this.Channel = channel;
            this.Sender = sender;
            this.Content = content;
            this.Chunks = chunks;
        }

        public static ServerMessage Decode(byte[] bytes) {
            return MessagePackSerializer.Deserialize<ServerMessage>(bytes);
        }

        protected override byte[] PayloadEncode() {
            return MessagePackSerializer.Serialize(this);
        }

        public SenderPlayer? GetSenderPlayer() {
            using var stream = new MemoryStream(this.Sender);
            using var reader = new BinaryReader(stream);

            var text = new List<byte>();

            while (reader.BaseStream.Position < reader.BaseStream.Length) {
                var b = reader.ReadByte();

                // read payloads
                if (b == 2) {
                    var chunkType = reader.ReadByte();
                    var chunkLen = XivString.GetInteger(reader);

                    // interactive
                    if (chunkType == 0x27) {
                        var subType = reader.ReadByte();
                        // player name
                        if (subType == 0x01) {
                            // unk
                            reader.ReadByte();

                            var serverId = (ushort) XivString.GetInteger(reader);

                            // unk
                            reader.ReadBytes(2);

                            var nameLen = (int) XivString.GetInteger(reader);
                            var playerName = Encoding.UTF8.GetString(reader.ReadBytes(nameLen));

                            return new SenderPlayer(playerName, serverId);
                        }
                    }

                    reader.ReadBytes((int) chunkLen);
                    continue;
                }

                // read text
                text.Add(b);
            }

            if (text.Count == 0) {
                return null;
            }

            var name = Encoding.UTF8.GetString(text.ToArray());

            // remove the party position if present
            var chars = name.ToCharArray();
            if (chars.Length > 0 && PartyChars.Contains(chars[0])) {
                name = name.Substring(1);
            }

            return new SenderPlayer(name, 0);
        }

        private static readonly char[] PartyChars = [
            '\ue090', '\ue091', '\ue092', '\ue093', '\ue094', '\ue095', '\ue096', '\ue097',
        ];

        public class SenderPlayer : IComparable<SenderPlayer>, IComparable {
            public string Name { get; }
            public ushort Server { get; }

            public SenderPlayer(string name, ushort server) {
                this.Name = name;
                this.Server = server;
            }

            protected bool Equals(SenderPlayer other) {
                return this.Name == other.Name && this.Server == other.Server;
            }

            public override bool Equals(object? obj) {
                if (ReferenceEquals(null, obj)) {
                    return false;
                }

                if (ReferenceEquals(this, obj)) {
                    return true;
                }

                return obj.GetType() == this.GetType() && this.Equals((SenderPlayer) obj);
            }

            public override int GetHashCode() {
                unchecked {
                    return (this.Name.GetHashCode() * 397) ^ this.Server;
                }
            }

            public int CompareTo(SenderPlayer? other) {
                if (ReferenceEquals(this, other)) {
                    return 0;
                }

                if (ReferenceEquals(null, other)) {
                    return 1;
                }

                var nameComparison = string.Compare(this.Name, other.Name, StringComparison.Ordinal);
                return nameComparison != 0 ? nameComparison : this.Server.CompareTo(other.Server);
            }

            public int CompareTo(object? obj) {
                if (ReferenceEquals(null, obj)) {
                    return 1;
                }

                if (ReferenceEquals(this, obj)) {
                    return 0;
                }

                return obj is SenderPlayer other ? this.CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(SenderPlayer)}");
            }
        }
    }
}
