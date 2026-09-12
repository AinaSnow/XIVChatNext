using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using XIVChatCommon;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;

namespace XIVChatPlugin {
    // Build static indexes on a worker against a captured GameData/language; publish only complete generations.
    internal sealed class GameCardCatalog : IDisposable {
        private readonly GameSheetSource data;
        private readonly LinkMetadataCache metadata;
        private string scope = "";
        private Task<GameCardCatalog>? indexing;
        private CancellationTokenSource buildCancellation = new();
        private bool started;
        internal bool Ready { get; private set; }
        internal bool Failed { get; private set; }
        internal Exception? Error { get; private set; }
        private Dictionary<uint, List<uint>> recipes = new();
        private Dictionary<uint, List<(uint Shop, ushort Row)>> shops = new();
        private Dictionary<uint, List<uint>> gatheringItems = new();
        private Dictionary<uint, List<uint>> gatheringBases = new();
        private Dictionary<uint, List<uint>> gatheringPoints = new();
        private Dictionary<uint, List<uint>> npcs = new();
        private Dictionary<(Type, uint), List<uint>> locations = new();
        private readonly BoundedCache<(uint, uint), IReadOnlyList<CardItemSource>> sources = new(128);
        internal GameCardCatalog(IDataManager data, LinkMetadataCache metadata) : this(new GameSheetSource(data), metadata) { }
        internal GameCardCatalog(GameSheetSource data, LinkMetadataCache metadata) { this.data = data; this.metadata = metadata; }
        internal void RefreshScope() {
            this.metadata.RefreshScope();
            if (this.scope == this.metadata.Scope) return;
            this.CancelBuild(); this.indexing = null; this.started = this.Ready = this.Failed = false; this.Error = null;
            this.scope = this.metadata.Scope;
            this.recipes.Clear(); this.shops.Clear(); this.gatheringItems.Clear(); this.gatheringBases.Clear();
            this.gatheringPoints.Clear(); this.npcs.Clear(); this.locations.Clear(); this.sources.Clear();
        }
        internal void Advance() {
            this.RefreshScope();
            if (this.Ready || this.Failed) return;
            if (!this.started) {
                this.started = true;
                var frozen = this.data.Freeze(); var token = this.buildCancellation.Token;
                this.indexing = Task.Run(() => {
                    var builder = new GameCardCatalog(frozen, new LinkMetadataCache(frozen));
                    try { foreach (var ignored in builder.Build()) token.ThrowIfCancellationRequested(); return builder; }
                    finally { builder.buildCancellation.Dispose(); }
                }, token);
            }
            if (!this.indexing!.IsCompleted) return;
            if (this.indexing.IsCompletedSuccessfully) {
                var complete = this.indexing.Result;
                this.recipes = complete.recipes; this.shops = complete.shops; this.gatheringItems = complete.gatheringItems;
                this.gatheringBases = complete.gatheringBases; this.gatheringPoints = complete.gatheringPoints;
                this.npcs = complete.npcs; this.locations = complete.locations; this.Ready = true;
            } else { this.Failed = true; this.Error = this.indexing.Exception; }
        }
        private static void Add<TKey, TValue>(Dictionary<TKey, List<TValue>> index, TKey key, TValue value) where TKey : notnull {
            if (!index.TryGetValue(key, out var rows)) index[key] = rows = new();
            rows.Add(value);
        }
        private IEnumerable<int> Build() {
            foreach (var recipe in this.data.GetExcelSheet<Recipe>()) {
                if (recipe.ItemResult.RowId != 0 && recipe.AmountResult > 0) Add(this.recipes, recipe.ItemResult.RowId, recipe.RowId);
                yield return 0;
            }
            foreach (var shop in this.data.GetSubrowExcelSheet<GilShopItem>()) foreach (var row in shop) {
                if (row.Item.RowId != 0) Add(this.shops, row.Item.RowId, (row.RowId, row.SubrowId));
                yield return 0;
            }
            foreach (var item in this.data.GetExcelSheet<GatheringItem>()) {
                if (item.Item.Is<Item>() && item.Item.RowId != 0) Add(this.gatheringItems, item.RowId, item.Item.RowId);
                yield return 0;
            }
            foreach (var point in this.data.GetExcelSheet<GatheringPointBase>()) {
                foreach (var item in point.Item) {
                    if (item.Is<GatheringItem>() && this.gatheringItems.TryGetValue(item.RowId, out var itemIds))
                        foreach (var itemId in itemIds) Add(this.gatheringBases, point.RowId, itemId);
                }
                yield return 0;
            }
            foreach (var point in this.data.GetExcelSheet<GatheringPoint>()) {
                if (point.TerritoryType.RowId != 0 && this.gatheringBases.TryGetValue(point.GatheringPointBase.RowId, out var items))
                    foreach (var itemId in items) Add(this.gatheringPoints, itemId, point.RowId);
                yield return 0;
            }
            foreach (var npc in this.data.GetExcelSheet<ENpcBase>()) {
                var found = new HashSet<uint>();
                foreach (var handler in npc.ENpcData) this.FindGilShops(handler, found, new HashSet<uint>(), 0);
                foreach (var shop in found) Add(this.npcs, shop, npc.RowId);
                yield return 0;
            }
            foreach (var level in this.data.GetExcelSheet<Level>()) {
                if (level.Map.RowId != 0 && (level.Object.Is<ENpcBase>() || level.Object.Is<GatheringPoint>()))
                    Add(this.locations, (level.Object.RowType!, level.Object.RowId), level.RowId);
                yield return 0;
            }
        }
        private void FindGilShops(RowRef handler, HashSet<uint> result, HashSet<uint> visited, int depth) {
            if (handler.RowId == 0 || depth > 4 || visited.Count > 128 || !visited.Add(handler.RowId)) return;
            if (handler.Is<GilShop>()) { result.Add(handler.RowId); return; }
            if (handler.Is<TopicSelect>() && handler.GetValueOrDefault<TopicSelect>() is { } topic)
                foreach (var shop in topic.Shop) this.FindGilShops(shop, result, visited, depth + 1);
            else if (handler.Is<PreHandler>() && handler.GetValueOrDefault<PreHandler>() is { } pre)
                this.FindGilShops(pre.Target, result, visited, depth + 1);
            // CustomTalk script arguments and exchange shops are intentionally not guessed.
        }
        internal ServerGameCard Read(ClientGameCard request) {
            this.RefreshScope();
            var reply = new ServerGameCard { RequestId = request.RequestId, Query = request.Query, Id = request.Id, ItemKind = request.ItemKind,
                DataScope = this.scope, Source = this.metadata.Source() };
            if (!request.Valid) { reply.Status = CardStatus.InvalidRequest; return reply; }
            if (!string.IsNullOrEmpty(request.DataScope) && request.DataScope != this.scope) { reply.Status = CardStatus.Stale; return reply; }
            switch (request.Query) {
                case CardQuery.Item:
                    reply.Item = this.metadata.ItemChunk(request.Id, (ItemKind)request.ItemKind);
                    if (reply.Item == null) reply.Status = CardStatus.NotFound;
                    break;
                case CardQuery.Map:
                    reply.Map = this.Map(request.Id);
                    if (reply.Map == null) reply.Status = CardStatus.NotFound;
                    break;
                case CardQuery.Recipes:
                case CardQuery.Sources:
                    if (this.Failed) { reply.Status = CardStatus.Unavailable; break; }
                    if (!this.Ready) { reply.Status = CardStatus.Busy; break; }
                    if (request.ItemKind == (uint)ItemKind.EventItem) break;
                    if (request.Query == CardQuery.Recipes) {
                        var ids = this.recipes.GetValueOrDefault(request.Id) ?? new List<uint>();
                        this.Page(reply, request.Page, ids.Count);
                        if (reply.Status == CardStatus.Success) reply.Recipes = ids.Skip(request.Page * CardProtocol.PageSize).Take(CardProtocol.PageSize)
                            .Select(this.Recipe).Where(r => r != null).Cast<CardRecipe>().ToArray();
                    } else {
                        var rows = this.sources.Get((request.Id, request.ItemKind), key => this.Sources(key.Item1));
                        this.Page(reply, request.Page, rows.Count);
                        if (reply.Status == CardStatus.Success) reply.Sources = rows.Skip(request.Page * CardProtocol.PageSize).Take(CardProtocol.PageSize).ToArray();
                    }
                    break;
                default: reply.Status = CardStatus.InvalidRequest; break;
            }
            return reply;
        }
        private void Page(ServerGameCard reply, int page, int total) {
            reply.Truncated = total > CardProtocol.MaxRelations;
            reply.PageCount = Math.Max(1, (Math.Min(total, CardProtocol.MaxRelations) + CardProtocol.PageSize - 1) / CardProtocol.PageSize);
            if (page >= reply.PageCount) { reply.Status = CardStatus.InvalidRequest; reply.PageCount = 1; }
            else reply.Page = page;
        }
        internal CardMap? Map(uint id) {
            var row = this.data.GetExcelSheet<Map>().GetRowOrDefault(id);
            if (row == null) return null;
            return new CardMap { Id = id, Filename = row.Value.Id.ExtractText(), SizeFactor = row.Value.SizeFactor,
                Name = CardProtocol.Text(row.Value.PlaceName.ValueNullable?.Name.ExtractText(), 512), TerritoryId = row.Value.TerritoryType.RowId,
                PlaceNameId = row.Value.PlaceName.RowId };
        }
        private CardMap? Location(Type type, uint id, uint? fallbackTerritory = null, string? fallbackName = null) {
            if (this.locations.TryGetValue((type, id), out var ids)) {
                // Multiple spawns/floors cannot be represented as a single certain location.
                var rows = ids.Select(v => this.data.GetExcelSheet<Level>().GetRowOrDefault(v)).Where(v => v != null).Select(v => v!.Value).ToArray();
                var positions = rows.Select(v => (v.Map.RowId, v.X, v.Z)).Distinct().ToArray();
                if (positions.Length == 1 && this.Map(positions[0].RowId) is { SizeFactor: > 0 } map &&
                    this.data.GetExcelSheet<Map>().GetRowOrDefault(map.Id) is { } mapRow) {
                    var coordinates = MapUtil.WorldToMap(new Vector2(positions[0].X, positions[0].Z), mapRow);
                    map.X = coordinates.X; map.Y = coordinates.Y; return map;
                }
            }
            if (fallbackTerritory is > 0) {
                var territory = this.data.GetExcelSheet<TerritoryType>().GetRowOrDefault(fallbackTerritory.Value);
                return new CardMap { TerritoryId = fallbackTerritory.Value, Name = CardProtocol.Text(territory?.PlaceName.ValueNullable?.Name.ExtractText() ?? fallbackName, 512),
                    PlaceNameId = territory?.PlaceName.RowId ?? 0 };
            }
            return null;
        }
        private CardItemRef ItemRef(uint id) {
            var row = this.data.GetExcelSheet<Item>().GetRowOrDefault(id);
            return new CardItemRef { Id = id, Name = CardProtocol.Text(row?.Name.ExtractText()), Icon = row?.Icon ?? 0 };
        }
        private CardRecipe? Recipe(uint id) {
            if (this.data.GetExcelSheet<Recipe>().GetRowOrDefault(id) is not { } row) return null;
            var ingredients = new List<CardIngredient>();
            for (int i = 0; i < row.Ingredient.Count && i < row.AmountIngredient.Count; i++)
                if (row.Ingredient[i].RowId > 0 && row.AmountIngredient[i] > 0)
                    ingredients.Add(new CardIngredient { Item = this.ItemRef(row.Ingredient[i].RowId), Quantity = row.AmountIngredient[i] });
            return new CardRecipe { Id = id, CraftTypeId = row.CraftType.RowId, CraftJob = CardProtocol.Text(row.CraftType.ValueNullable?.Name.ExtractText()),
                Level = row.RecipeLevelTable.ValueNullable?.ClassJobLevel ?? 0, Stars = row.RecipeLevelTable.ValueNullable?.Stars ?? 0,
                Yield = row.AmountResult, Ingredients = ingredients.ToArray(), RequiresUnlock = row.SecretRecipeBook.RowId != 0 || row.Quest.RowId != 0 || row.IsSpecializationRequired };
        }
        private IReadOnlyList<CardItemSource> Sources(uint itemId) {
            var result = new List<CardItemSource>();
            var item = this.data.GetExcelSheet<Item>().GetRowOrDefault(itemId);
            if (this.shops.TryGetValue(itemId, out var shops)) foreach (var (shopId, subrow) in shops) {
                var shop = this.data.GetExcelSheet<GilShop>().GetRowOrDefault(shopId);
                var row = this.data.GetSubrowExcelSheet<GilShopItem>().GetSubrowOrDefault(shopId, subrow);
                if (shop == null || row == null) continue;
                var vendors = this.npcs.GetValueOrDefault(shopId)?.Distinct().ToArray() ?? Array.Empty<uint>();
                if (vendors.Length == 0) vendors = new uint[] { 0 };
                foreach (var vendor in vendors) {
                    result.Add(new CardItemSource { Kind = ItemSourceKind.GilShop, Id = shopId, Name = CardProtocol.Text(shop.Value.Name.ExtractText()),
                        NpcId = vendor, NpcName = CardProtocol.Text(this.data.GetExcelSheet<ENpcResident>().GetRowOrDefault(vendor)?.Singular.ExtractText()),
                        GilPrice = row.Value.IsHQ ? null : item?.PriceMid, ItemKind = row.Value.IsHQ ? (uint)ItemKind.Hq : 0,
                        RequiresUnlock = shop.Value.Quest.RowId != 0 || shop.Value.FestivalId != 0 || row.Value.QuestRequired.Any(q => q.RowId != 0) ||
                            row.Value.AchievementRequired.RowId != 0 || row.Value.StateRequired != 0,
                        Location = this.Location(typeof(ENpcBase), vendor) });
                    if (result.Count > CardProtocol.MaxRelations) return result;
                }
            }
            if (this.gatheringPoints.TryGetValue(itemId, out var points)) foreach (var pointId in points.Distinct()) {
                if (this.data.GetExcelSheet<GatheringPoint>().GetRowOrDefault(pointId) is not { } point ||
                    point.GatheringPointBase.ValueNullable is not { } basis) continue;
                var transient = this.data.GetExcelSheet<GatheringPointTransient>().GetRowOrDefault(pointId);
                result.Add(new CardItemSource { Kind = ItemSourceKind.Gathering, Id = pointId,
                    Name = CardProtocol.Text(basis.GatheringType.ValueNullable?.Name.ExtractText()), GatheringTypeId = basis.GatheringType.RowId,
                    GatheringLevel = basis.GatheringLevel, Location = this.Location(typeof(GatheringPoint), pointId, point.TerritoryType.RowId,
                        point.PlaceName.ValueNullable?.Name.ExtractText()),
                    TimedOrHidden = transient?.GatheringRarePopTimeTable.RowId > 0 || transient?.EphemeralStartTime != transient?.EphemeralEndTime });
                if (result.Count > CardProtocol.MaxRelations) return result;
            }
            return result;
        }
        private void CancelBuild() {
            this.buildCancellation.Cancel(); this.buildCancellation.Dispose(); this.buildCancellation = new();
            if (this.indexing != null) _ = this.indexing.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
        public void Dispose() { this.CancelBuild(); this.buildCancellation.Dispose(); }
    }
}
