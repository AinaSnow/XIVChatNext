using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
using XIVChatCommon.Message;

namespace XIVChatCommon {
    public enum CardQuery : byte { Item, Recipes, Sources, Map, Equipment }
    public enum CardStatus : byte { Success, NotFound, Unavailable, Busy, InvalidRequest, Stale }
    public enum ItemSourceKind : byte { GilShop, Gathering }

    [MessagePackObject]
    public sealed class CardItemRef {
        [Key(0)] public uint Id { get; set; }
        [Key(1)] public string Name { get; set; } = "";
        [Key(2)] public uint Icon { get; set; }
        [Key(3)] public uint Kind { get; set; }
    }
    [MessagePackObject]
    public sealed class CardIngredient {
        [Key(0)] public CardItemRef Item { get; set; } = new();
        [Key(1)] public int Quantity { get; set; }
    }
    [MessagePackObject]
    public sealed class CardRecipe {
        [Key(0)] public uint Id { get; set; }
        [Key(1)] public uint CraftTypeId { get; set; }
        [Key(2)] public string CraftJob { get; set; } = "";
        [Key(3)] public int Level { get; set; }
        [Key(4)] public int Stars { get; set; }
        [Key(5)] public int Yield { get; set; }
        [Key(6)] public CardIngredient[] Ingredients { get; set; } = Array.Empty<CardIngredient>();
        [Key(7)] public bool RequiresUnlock { get; set; }
    }
    [MessagePackObject]
    public sealed class CardMap {
        [Key(0)] public uint Id { get; set; }
        [Key(1)] public string Name { get; set; } = "";
        [Key(2)] public string? Filename { get; set; }
        [Key(3)] public ushort SizeFactor { get; set; }
        [Key(4)] public uint TerritoryId { get; set; }
        [Key(5)] public float? X { get; set; }
        [Key(6)] public float? Y { get; set; }
        [Key(7)] public uint PlaceNameId { get; set; }
    }
    [MessagePackObject]
    public sealed class CardItemSource {
        [Key(0)] public ItemSourceKind Kind { get; set; }
        [Key(1)] public uint Id { get; set; }
        [Key(2)] public string Name { get; set; } = "";
        [Key(3)] public uint NpcId { get; set; }
        [Key(4)] public string NpcName { get; set; } = "";
        [Key(5)] public uint? GilPrice { get; set; }
        [Key(6)] public CardMap? Location { get; set; }
        [Key(7)] public bool RequiresUnlock { get; set; }
        [Key(8)] public uint ItemKind { get; set; }
        [Key(9)] public int GatheringLevel { get; set; }
        [Key(10)] public uint GatheringTypeId { get; set; }
        [Key(11)] public bool TimedOrHidden { get; set; }
    }
    [MessagePackObject]
    public sealed class CardMateria {
        [Key(0)] public int Slot { get; set; }
        [Key(1)] public CardItemRef Item { get; set; } = new();
        [Key(2)] public uint ParameterId { get; set; }
        [Key(3)] public string ParameterName { get; set; } = "";
        [Key(4)] public int Value { get; set; }
    }
    [MessagePackObject]
    public sealed class CardEquippedItem {
        [Key(0)] public int Slot { get; set; }
        [Key(1)] public TextChunk Item { get; set; } = new("");
        [Key(2)] public CardMateria[] Materia { get; set; } = Array.Empty<CardMateria>();
        [Key(3)] public bool HasCustomStats { get; set; }
    }
    [MessagePackObject]
    public sealed class EquipmentSnapshot {
        [Key(0)] public string OwnerKey { get; set; } = "";
        [Key(1)] public string OwnerEpoch { get; set; } = "";
        [Key(2)] public uint ClassJobId { get; set; }
        [Key(3)] public int Level { get; set; }
        [Key(4)] public long CapturedAtUnixMilliseconds { get; set; }
        [Key(5)] public bool IsLive { get; set; }
        [Key(6)] public CardEquippedItem[] Items { get; set; } = Array.Empty<CardEquippedItem>();
        [Key(7)] public long Revision { get; set; }
        [Key(8)] public bool IsLevelSynced { get; set; }
    }

    public static class CardProtocol {
        public const int PageSize = 8;
        public const int MaxRelations = 2048;
        public const int MaxPacketBytes = 96_000;
        public static bool ValidKind(uint kind) => kind is 0 or 500_000 or 1_000_000 or 2_000_000;
        public static bool ValidItem(uint id, uint kind) => ValidKind(kind) &&
            (kind == 2_000_000 ? id is >= 2_000_000 and < 3_000_000 : id is > 0 and < 500_000);
        public static string Text(string? value, int length = 256) => value == null ? "" : value.Length <= length ? value : value.Substring(0, length);
        public static bool ValidMap(CardMap? map) => map == null || (map.Name?.Length <= 512 && (map.Filename == null || map.Filename.Length <= 64) &&
            (!map.X.HasValue || float.IsFinite(map.X.Value)) && (!map.Y.HasValue || float.IsFinite(map.Y.Value)));
        public static bool ValidItemData(TextChunk? item) => item == null || (ValidItem(item.ItemId ?? 0, item.ItemKind ?? 0) &&
            item.ItemName?.Length <= 512 && (item.ItemDescription == null || item.ItemDescription.Length <= 8192) &&
            (item.ItemCategory == null || item.ItemCategory.Length <= 512) &&
            (item.ItemStats == null || (item.ItemStats.Count <= 32 && item.ItemStats.All(s => s?.Length <= 512))) &&
            (item.ItemDetails == null || (item.ItemDetails.Parameters?.Count <= 32 && item.ItemDetails.EquipSlots?.Length <= 14 &&
             item.ItemDetails.AllowedJobs?.Length <= 64 && item.ItemDetails.ClassJobs?.Length <= 512 &&
             item.ItemDetails.EquipSlots.All(s => s is >= 0 and < 14) && item.ItemDetails.Parameters.All(p => p != null && p.Name?.Length <= 256 &&
             Math.Abs((long)p.NqValue) < 1_000_000 && Math.Abs((long)p.HqDelta) < 1_000_000))));
        public static bool ValidEquipment(EquipmentSnapshot? snapshot) => snapshot != null && snapshot.OwnerKey?.Length is > 0 and <= 128 &&
            snapshot.OwnerEpoch?.Length is > 0 and <= 64 && snapshot.Items?.Length <= 14 && snapshot.Revision >= 0 &&
            snapshot.Level is >= 0 and <= 1000 && snapshot.Items.All(i => i != null && i.Slot is >= 0 and < 14 && i.Materia?.Length <= 5 && i.Item != null && ValidItemData(i.Item) &&
                i.Materia.All(m => m != null && m.Slot is >= 0 and < 5 && m.Item != null && ValidItem(m.Item.Id, m.Item.Kind) && m.Item.Name?.Length <= 256 && m.ParameterName?.Length <= 256 && Math.Abs((long)m.Value) < 1_000_000)) &&
            snapshot.Items.Select(i => i.Slot).Distinct().Count() == snapshot.Items.Length;
    }

    public enum GearComparisonReason { Comparable, NoSnapshot, MissingDetails, WrongSlot, WrongJob, LevelTooLow, Weapon, SpecialEquipment }
    public sealed record GearParameterDifference(uint Id, string Name, int Candidate, int Equipped) {
        public int Delta => this.Candidate - this.Equipped;
    }
    public sealed record GearComparison(GearComparisonReason Reason, IReadOnlyList<GearParameterDifference> Parameters);
    public static class GearComparer {
        public static GearComparison Compare(TextChunk candidate, EquipmentSnapshot? snapshot, CardEquippedItem? equipped) {
            GearComparison Stop(GearComparisonReason reason) => new(reason, Array.Empty<GearParameterDifference>());
            if (snapshot == null || equipped == null) return Stop(GearComparisonReason.NoSnapshot);
            var details = candidate.ItemDetails; var old = equipped.Item.ItemDetails;
            if (details == null || old == null || details.Parameters.Count == 0 || old.Parameters.Count == 0) return Stop(GearComparisonReason.MissingDetails);
            if (!details.EquipSlots.Contains(equipped.Slot)) return Stop(GearComparisonReason.WrongSlot);
            if (equipped.Slot is 0 or 1) return Stop(GearComparisonReason.Weapon);
            var ring = details.EquipSlots.All(s => s is 11 or 12);
            if (equipped.HasCustomStats || details.SpecialEquipment || old.SpecialEquipment || equipped.Slot == 13 ||
                (!ring && (details.EquipSlots.Length != 1 || old.EquipSlots.Length != 1))) return Stop(GearComparisonReason.SpecialEquipment);
            if (details.AllowedJobs.Length == 0 || !details.AllowedJobs.Contains(snapshot.ClassJobId)) return Stop(GearComparisonReason.WrongJob);
            if ((candidate.ItemEquipLevel ?? 0) > snapshot.Level) return Stop(GearComparisonReason.LevelTooLow);
            var hq = candidate.ItemKind == (uint)GameItemKind.Hq || (candidate.ItemKind == null && candidate.IsHq == true);
            var oldHq = equipped.Item.ItemKind == (uint)GameItemKind.Hq;
            var result = details.Parameters.Select(p => p.Id).Union(old.Parameters.Select(p => p.Id)).Select(id => {
                var current = details.Parameters.FirstOrDefault(p => p.Id == id); var previous = old.Parameters.FirstOrDefault(p => p.Id == id);
                return new GearParameterDifference(id, current?.Name ?? previous!.Name, current?.Value(hq) ?? 0, previous?.Value(oldHq) ?? 0);
            }).ToArray();
            return new GearComparison(GearComparisonReason.Comparable, result);
        }
    }
}
