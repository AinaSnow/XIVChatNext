using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using XIVChatCommon;
using XIVChatCommon.Message;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel.Sheets;

namespace XIVChatPlugin {
    internal sealed record ItemMetadata(string Name, string Description, uint Icon, ushort Level, byte Rarity,
        string Category, ushort EquipLevel, byte MateriaSlots, bool AdvancedMelding, List<string> Stats, GameItemDetails Details, GameDataSource Source);
    internal sealed record MapMetadata(uint Id, string? Filename, ushort? SizeFactor, string? PlaceName);

    internal sealed class LinkMetadataCache {
        private readonly GameSheetSource data;
        private object? gameData;
        private int language = -1;
        private string? version;
        internal string Scope { get; private set; } = "";
        internal GameDataSource Source() => new() {
            Language = this.data.Language.ToString(), Version = this.version,
            RetrievedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        private readonly BoundedCache<(uint Id, ItemKind Kind), ItemMetadata?> items = new(2048);
        private readonly BoundedCache<(uint Map, uint Territory), MapMetadata> maps = new(512);
        internal (int Items, int Maps, long Hits, long Misses) Usage =>
            (this.items.Count, this.maps.Count, this.items.Hits + this.maps.Hits, this.items.Misses + this.maps.Misses);
        internal LinkMetadataCache(IDataManager data) : this(new GameSheetSource(data)) { }
        internal LinkMetadataCache(GameSheetSource data) => this.data = data;

        // A loaded GameData instance is tied to one installed game-data version. Nothing persists across it.
        internal bool RefreshScope() {
            if (ReferenceEquals(this.gameData, this.data.GameData) && this.language == (int)this.data.Language) return false;
            this.gameData = this.data.GameData; this.language = (int)this.data.Language;
            this.Scope = Guid.NewGuid().ToString("N");
            this.version = null;
            try {
                var path = Path.Combine(this.data.GameData.DataPath.Parent!.FullName, "ffxivgame.ver");
                var value = File.ReadAllText(path).Trim();
                if (value.Length is > 0 and <= 100) this.version = value;
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
            this.items.Clear(); this.maps.Clear(); return true;
        }
        internal ItemMetadata? Item(uint id, ItemKind kind) {
            this.RefreshScope();
            return this.items.Get((id, kind), key => this.ReadItem(key.Id, key.Kind));
        }
        internal TextChunk? ItemChunk(uint id, ItemKind kind) {
            var item = this.Item(id, kind);
            return item == null ? null : new TextChunk(item.Name) {
                ItemId = id, ItemKind = (uint)kind, IsHq = kind == ItemKind.Hq,
                ItemName = CardProtocol.Text(item.Name, 512), ItemDescription = CardProtocol.Text(item.Description, 8192),
                ItemIconId = item.Icon, ItemLevel = item.Level, ItemRarity = item.Rarity, ItemCategory = item.Category,
                ItemEquipLevel = item.EquipLevel, ItemMateriaSlots = item.MateriaSlots, ItemIsAdvancedMeldingPermitted = item.AdvancedMelding,
                ItemStats = item.Stats, ItemDetails = item.Details, DataSource = item.Source,
            };
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
                if (eventItem == null) return null;
                var name = eventItem.Value.Name.ExtractText();
                if (string.IsNullOrEmpty(name)) name = eventItem.Value.Singular.ExtractText();
                return new ItemMetadata(name, "", eventItem.Value.Icon, 0, 0, "", 0, 0, false,
                    new List<string>(), new GameItemDetails(), this.Source());
            }
            var item = this.data.GetExcelSheet<Item>().GetRowOrDefault(id);
            if (item == null) return null;
            var row = item.Value;
            var values = new List<ItemParameter>();
            var bonuses = new List<ItemParameter>();
            void Add(uint parameterId, int value) {
                if (value == 0) return;
                var name = this.data.GetExcelSheet<BaseParam>().GetRowOrDefault(parameterId)?.Name.ExtractText() ?? $"#{parameterId}";
                values.Add(new ItemParameter { Id = parameterId, Name = name, NqValue = value });
            }
            Add(12, row.DamagePhys); Add(13, row.DamageMag);
            Add(21, row.DefensePhys); Add(24, row.DefenseMag);
            for (var i = 0; i < row.BaseParam.Count && i < row.BaseParamValue.Count; i++) {
                var parameter = row.BaseParam[i].ValueNullable;
                if (parameter is { RowId: > 0 } p && row.BaseParamValue[i] != 0)
                    values.Add(new ItemParameter { Id = p.RowId, Name = p.Name.ExtractText(), NqValue = row.BaseParamValue[i] });
            }
            // Special parameters on equipment are HQ deltas, including weapon damage / defense.
            // Consumable effects have separate action/food semantics and are not equipment bonuses.
            if (row.CanBeHq && row.EquipSlotCategory.RowId != 0) {
                for (var i = 0; i < row.BaseParamSpecial.Count && i < row.BaseParamValueSpecial.Count; i++) {
                    var parameter = row.BaseParamSpecial[i].ValueNullable;
                    if (parameter is { RowId: > 0 } p && row.BaseParamValueSpecial[i] != 0)
                        bonuses.Add(new ItemParameter { Id = p.RowId, Name = p.Name.ExtractText(), HqDelta = row.BaseParamValueSpecial[i] });
                }
            }
            var details = new GameItemDetails {
                Parameters = GameItemDetails.Merge(values, bonuses), EquipSlotCategoryId = row.EquipSlotCategory.RowId,
                ClassJobs = row.ClassJobCategory.ValueNullable?.Name.ExtractText() ?? "", CanBeHq = row.CanBeHq,
                ClassJobCategoryId = row.ClassJobCategory.RowId, ItemCategoryId = row.ItemUICategory.RowId,
                // Row 1 is the ordinary empty bonus / unrestricted race-and-gender entry.
                // Treating any nonzero link as special suppresses comparisons for normal gear.
                SpecialEquipment = row.ItemSpecialBonus.RowId > 1 || row.EquipRestriction.RowId > 1 || row.Rarity == 7 ||
                    values.Any(p => p.Id is 55 or 56), // Level/job-dependent primary and secondary attribute correction.
            };
            if (row.EquipSlotCategory.ValueNullable is { } slot) {
                var flags = new[] { slot.MainHand, slot.OffHand, slot.Head, slot.Body, slot.Gloves, slot.Waist,
                    slot.Legs, slot.Feet, slot.Ears, slot.Neck, slot.Wrists, slot.FingerR, slot.FingerL, slot.SoulCrystal };
                details.EquipSlots = flags.Select((value, index) => (value, index)).Where(v => v.value > 0).Select(v => v.index).ToArray();
            }
            if (row.ClassJobCategory.ValueNullable is { } jobCategory) {
                // Category column identifiers use English abbreviations, independently of displayed sheet language.
                details.AllowedJobs = this.data.GameData.Excel.GetSheet<ClassJob>(Lumina.Data.Language.English).Where(job => {
                    var abbreviation = job.Abbreviation.ExtractText();
                    var column = typeof(ClassJobCategory).GetProperty(abbreviation);
                    // The installed API 14 sheet schema still names the BST (job 43) flag Unknown0.
                    // Prefer the named field when a newer schema exposes it; do not infer other jobs.
                    if (column == null && job.RowId == 43 && abbreviation == "BST")
                        column = typeof(ClassJobCategory).GetProperty("Unknown0");
                    return column?.GetValue(jobCategory) is true;
                }).Select(job => job.RowId).Take(64).ToArray();
            }
            var stats = details.Parameters.Where(p => p.Value(kind == ItemKind.Hq) != 0)
                .Select(p => $"{p.Name} {p.Value(kind == ItemKind.Hq):+0;-0;0}").ToList();
            return new ItemMetadata(row.Name.ExtractText(), row.Description.ExtractText(), row.Icon, (ushort)row.LevelItem.RowId,
                row.Rarity, row.ItemUICategory.ValueNullable?.Name.ExtractText() ?? "", row.LevelEquip,
                row.MateriaSlotCount, row.IsAdvancedMeldingPermitted, stats, details, this.Source());
        }
    }
}
