using MessagePack;

namespace XIVChatCommon.Message.Client {
    [MessagePackObject]
    public sealed class ClientGameCard : Encodable {
        [Key(0)] public string RequestId { get; set; } = "";
        [Key(1)] public CardQuery Query { get; set; }
        [Key(2)] public uint Id { get; set; }
        [Key(3)] public uint ItemKind { get; set; }
        [Key(4)] public int Page { get; set; }
        [Key(5)] public string? DataScope { get; set; }
        [Key(6)] public string? OwnerKey { get; set; }
        [Key(7)] public string? OwnerEpoch { get; set; }
        [IgnoreMember] public bool Valid => this.RequestId?.Length is > 0 and <= 64 && System.Enum.IsDefined(this.Query) &&
            this.Page is >= 0 and < CardProtocol.MaxRelations / CardProtocol.PageSize &&
            (this.Query is CardQuery.Recipes or CardQuery.Sources || this.Page == 0) &&
            (this.Page == 0 || !string.IsNullOrEmpty(this.DataScope)) && (this.DataScope == null || this.DataScope.Length <= 256) &&
            (this.OwnerKey == null || this.OwnerKey.Length <= 128) && (this.OwnerEpoch == null || this.OwnerEpoch.Length <= 64) &&
            (this.Query == CardQuery.Equipment ? this.OwnerKey?.Length > 0 && this.OwnerEpoch?.Length > 0 :
                this.Query == CardQuery.Map ? this.Id is > 0 and < 100_000 : CardProtocol.ValidItem(this.Id, this.ItemKind));
        [IgnoreMember] protected override byte Code => (byte)ClientOperation.GameCard;
        protected override byte[] PayloadEncode() => MessagePackSerializer.Serialize(this);
        public static ClientGameCard Decode(byte[] bytes) => MessagePackSerializer.Deserialize<ClientGameCard>(bytes);
    }
}
