using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel.Sheets;

namespace XIVChatPlugin {
    internal sealed record ItemMetadata(string Name, string Description, uint Icon, ushort Level, byte Rarity,
        string Category, ushort EquipLevel, byte MateriaSlots, bool AdvancedMelding, List<string> Stats);
    internal sealed record MapMetadata(uint Id, string? Filename, ushort? SizeFactor, string? PlaceName);

    internal sealed class LinkMetadataCache {
        private readonly IDataManager data;
        private object? gameData;
        private int language = -1;
        private readonly BoundedCache<(uint Id, ItemKind Kind), ItemMetadata?> items = new(2048);
        private readonly BoundedCache<(uint Map, uint Territory), MapMetadata> maps = new(512);
        internal (int Items, int Maps, long Hits, long Misses) Usage =>
            (this.items.Count, this.maps.Count, this.items.Hits + this.maps.Hits, this.items.Misses + this.maps.Misses);
        internal LinkMetadataCache(IDataManager data) => this.data = data;

        // A loaded GameData instance is tied to one installed game-data version. Nothing persists across it.
        internal bool RefreshScope() {
            if (ReferenceEquals(this.gameData, this.data.GameData) && this.language == (int)this.data.Language) return false;
            this.gameData = this.data.GameData; this.language = (int)this.data.Language;
            this.items.Clear(); this.maps.Clear(); return true;
        }
        internal ItemMetadata? Item(uint id, ItemKind kind) {
            this.RefreshScope();
            return this.items.Get((id, kind), key => this.ReadItem(key.Id, key.Kind));
        }
        internal MapMetadata Map(uint map, uint territory) {
            this.RefreshScope();
            return this.maps.Get((map, territory), key => {
                var id = key.Map != 0 ? key.Map : this.data.GetExcelSheet<TerritoryType>().GetRowOrDefault(key.Territory)?.Map.RowId ?? 0;
                var row = id > 0 ? this.data.GetExcelSheet<Map>().GetRowOrDefault(id) : null;
                return new MapMetadata(id, row?.Id.ExtractText(), row?.SizeFactor, row?.PlaceName.ValueNullable?.Name.ExtractText());
            });
        }
        private ItemMetadata? ReadItem(uint id, ItemKind kind) {
            if (kind == ItemKind.EventItem) {
                var eventItem = this.data.GetExcelSheet<EventItem>().GetRowOrDefault(id);
                return eventItem == null ? null : new ItemMetadata(eventItem.Value.Name.ExtractText(), "",
                    eventItem.Value.Icon, 0, 0, "", 0, 0, false, new List<string>());
            }
            var item = this.data.GetExcelSheet<Item>().GetRowOrDefault(id);
            if (item == null) return null;
            var row = item.Value;
            var stats = new List<string>();
            // Keep the existing detail labels; richer localized game cards are a later phase.
            if (row.DamagePhys > 0) stats.Add($"物理基本性能 {row.DamagePhys}");
            if (row.DamageMag > 0 && row.DamageMag != row.DamagePhys) stats.Add($"魔法基本性能 {row.DamageMag}");
            if (row.DefensePhys > 0) stats.Add($"物理防御力 {row.DefensePhys}");
            if (row.DefenseMag > 0 && row.DefenseMag != row.DefensePhys) stats.Add($"魔法防御力 {row.DefenseMag}");
            for (var i = 0; i < row.BaseParam.Count && i < row.BaseParamValue.Count; i++) {
                var parameter = row.BaseParam[i].ValueNullable;
                if (parameter is { RowId: > 0 } p && row.BaseParamValue[i] > 0)
                    stats.Add($"{p.Name.ExtractText()} +{row.BaseParamValue[i]}");
            }
            if (kind == ItemKind.Hq) {
                for (var i = 0; i < row.BaseParamSpecial.Count && i < row.BaseParamValueSpecial.Count; i++) {
                    var parameter = row.BaseParamSpecial[i].ValueNullable;
                    if (parameter is { RowId: > 0 } p && row.BaseParamValueSpecial[i] > 0)
                        stats.Add($"{p.Name.ExtractText()} +{row.BaseParamValueSpecial[i]} (HQ)");
                }
            }
            return new ItemMetadata(row.Name.ExtractText(), row.Description.ExtractText(), row.Icon, (ushort)row.LevelItem.RowId,
                row.Rarity, row.ItemUICategory.ValueNullable?.Name.ExtractText() ?? "", row.LevelEquip,
                row.MateriaSlotCount, row.IsAdvancedMeldingPermitted, stats);
        }
    }
}
