using System;
using System.Linq;
using MessagePack;

namespace XIVChatCommon.Message.Server {
    [MessagePackObject]
    public sealed class ServerGameCard : Encodable {
        [Key(0)] public string RequestId { get; set; } = "";
        [Key(1)] public CardQuery Query { get; set; }
        [Key(2)] public uint Id { get; set; }
        [Key(3)] public uint ItemKind { get; set; }
        [Key(4)] public CardStatus Status { get; set; }
        [Key(5)] public int Page { get; set; }
        [Key(6)] public int PageCount { get; set; } = 1;
        [Key(7)] public string DataScope { get; set; } = "";
        [Key(8)] public GameDataSource? Source { get; set; }
        [Key(9)] public TextChunk? Item { get; set; }
        [Key(10)] public CardRecipe[] Recipes { get; set; } = Array.Empty<CardRecipe>();
        [Key(11)] public CardItemSource[] Sources { get; set; } = Array.Empty<CardItemSource>();
        [Key(12)] public CardMap? Map { get; set; }
        [Key(13)] public EquipmentSnapshot? Equipment { get; set; }
        [Key(14)] public bool Truncated { get; set; }
        [IgnoreMember] public bool Valid => this.RequestId?.Length <= 64 && Enum.IsDefined(this.Query) && Enum.IsDefined(this.Status) &&
            this.PageCount is >= 1 and <= CardProtocol.MaxRelations / CardProtocol.PageSize && this.Page >= 0 && this.Page < this.PageCount &&
            this.DataScope?.Length <= 256 && CardProtocol.ValidItemData(this.Item) && CardProtocol.ValidMap(this.Map) &&
            (this.Source == null || (this.Source.Provider?.Length <= 64 && this.Source.Language?.Length <= 32 &&
                (this.Source.Version == null || this.Source.Version.Length <= 128))) &&
            this.Recipes?.Length <= CardProtocol.PageSize && this.Sources?.Length <= CardProtocol.PageSize &&
            this.Recipes.All(r => r != null && r.Ingredients?.Length <= 12 && r.CraftJob?.Length <= 256 && r.Yield is > 0 and <= 999 &&
                r.Ingredients.All(i => i != null && i.Item != null && i.Item.Name?.Length <= 256 && i.Quantity is > 0 and <= 999 && CardProtocol.ValidItem(i.Item.Id, i.Item.Kind))) &&
            this.Sources.All(s => s != null && Enum.IsDefined(s.Kind) && CardProtocol.ValidKind(s.ItemKind) && s.Name?.Length <= 256 && s.NpcName?.Length <= 256 && CardProtocol.ValidMap(s.Location)) &&
            (this.Equipment == null || CardProtocol.ValidEquipment(this.Equipment)) &&
            (this.Status != CardStatus.Success || (this.Query switch {
                CardQuery.Item => this.Item != null && this.Item.ItemId == this.Id && (this.Item.ItemKind ?? 0) == this.ItemKind,
                CardQuery.Map => this.Map != null && this.Map.Id == this.Id && this.Id is > 0 and < 100_000,
                CardQuery.Equipment => this.Equipment != null,
                _ => CardProtocol.ValidItem(this.Id, this.ItemKind) && this.Source != null && this.DataScope.Length > 0,
            }));
        [IgnoreMember] protected override byte Code => (byte)ServerOperation.GameCard;
        protected override byte[] PayloadEncode() => MessagePackSerializer.Serialize(this);
        public static ServerGameCard Decode(byte[] bytes) => MessagePackSerializer.Deserialize<ServerGameCard>(bytes);
    }
}
