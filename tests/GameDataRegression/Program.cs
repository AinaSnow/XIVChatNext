using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina;
using Lumina.Excel.Sheets;
using XIVChatCommon;
using XIVChatCommon.Message.Client;
using XIVChatPlugin;

if (args.Length != 1) { Console.Error.WriteLine("Usage: GameDataRegression <installed game/sqpack directory>"); return 2; }
AssemblyLoadContext.Default.Resolving += (context, name) => {
    var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "addon", "Hooks", "dev", name.Name + ".dll");
    return File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;
};
using var gameData = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.English });
var manager = new GameSheetSource(gameData, ClientLanguage.English);
var metadata = new LinkMetadataCache(manager);
using var catalog = new GameCardCatalog(manager, metadata);
void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
ClientGameCard Request(CardQuery query, uint id, uint kind = 0) => new() { RequestId = Guid.NewGuid().ToString("N"), Query = query, Id = id, ItemKind = kind };
var items = manager.GetExcelSheet<Item>();
var helmetRow = items.GetRow(2753);
var helmet = metadata.ItemChunk(2753, ItemKind.Normal)!;
Console.WriteLine($"INFO Steel Sallet: restriction={helmetRow.EquipRestriction.RowId}, bonus={helmetRow.ItemSpecialBonus.RowId}, rarity={helmetRow.Rarity}, slots={string.Join(',', helmet.ItemDetails!.EquipSlots)}");
Check(GearComparer.Compare(helmet, new EquipmentSnapshot { ClassJobId = 43, Level = 50 },
    new CardEquippedItem { Slot = 2, Item = helmet }).Reason == GearComparisonReason.Comparable,
    "Ordinary Steel Sallet compares against itself for the live Beastmaster character");
var helmetHq = metadata.ItemChunk(2753, ItemKind.Hq)!;
var helmetComparison = GearComparer.Compare(helmetHq, new EquipmentSnapshot { ClassJobId = 43, Level = 50 },
    new CardEquippedItem { Slot = 2, Item = helmet });
Check(helmetComparison.Reason == GearComparisonReason.Comparable && helmetComparison.Parameters.Single(p => p.Id == 21).Delta == 6 &&
    helmetComparison.Parameters.Single(p => p.Id == 22).Delta == 1, "Real HQ helmet comparison includes defense and direct-hit deltas");
Check(metadata.ItemChunk(41081, ItemKind.Normal)!.ItemDetails!.SpecialEquipment,
    "Azeyma's Earrings with special scaling remain excluded from ordinary comparison");
Check(metadata.ItemChunk(50738, ItemKind.Normal)!.ItemDetails!.AllowedJobs.SequenceEqual(new uint[] { 43 }),
    "Beastmaster-specific weapon resolves only BST from the installed unnamed category flag");
var weapon = items.First(i => i.CanBeHq && i.DamagePhys > 0 && i.BaseParamSpecial.Any(p => p.RowId == 12));
var card = catalog.Read(Request(CardQuery.Item, weapon.RowId, (uint)ItemKind.Hq));
var index = Enumerable.Range(0, weapon.BaseParamSpecial.Count).First(i => weapon.BaseParamSpecial[i].RowId == 12);
Check(card.Valid && card.Item!.ItemDetails!.Parameters.Single(p => p.Id == 12).Value(true) == weapon.DamagePhys + weapon.BaseParamValueSpecial[index], "Installed game HQ weapon damage matches base plus special delta");
Check(card.Item!.ItemDetails!.AllowedJobs.Length > 0 && card.Item.ItemDetails.EquipSlots.Contains(0), "Installed equipment job and slot restrictions are resolved");
var frenchMetadata = new LinkMetadataCache(new GameSheetSource(gameData, ClientLanguage.French));
Check(frenchMetadata.ItemChunk(weapon.RowId, ItemKind.Hq)!.ItemDetails!.AllowedJobs.SequenceEqual(card.Item.ItemDetails.AllowedJobs), "Job restrictions are independent of translated class abbreviations");
var clock = Stopwatch.StartNew(); double slowest = 0; int frames = 0;
while (!catalog.Ready && !catalog.Failed && clock.Elapsed < TimeSpan.FromSeconds(60)) {
    var frame = Stopwatch.StartNew(); catalog.Advance(); slowest = Math.Max(slowest, frame.Elapsed.TotalMilliseconds); frames++;
    if (!catalog.Ready) Thread.Sleep(1);
}
Check(catalog.Ready, "Installed recipe/shop/gathering indexes complete: " + catalog.Error);
Console.WriteLine($"INFO Index frames={frames}, total={clock.ElapsedMilliseconds}ms, slowest step={slowest:F1}ms");
var recipe = manager.GetExcelSheet<Recipe>().First(r => r.ItemResult.RowId > 0 && r.AmountResult > 0 && r.Ingredient.Any(i => i.RowId > 0));
var recipes = catalog.Read(Request(CardQuery.Recipes, recipe.ItemResult.RowId));
Check(recipes.Valid && recipes.Recipes.Length > 0 && recipes.Recipes.All(r => r.Ingredients.Length > 0 && r.Level > 0), "Installed recipe resolves crafting level, yield and ingredients");
var gathering = manager.GetExcelSheet<GatheringItem>().First(r => r.Item.Is<Item>() && r.Item.RowId > 0 &&
    catalog.Read(Request(CardQuery.Sources, r.Item.RowId)).Sources.Any(s => s.Kind == ItemSourceKind.Gathering));
var gatheringSources = catalog.Read(Request(CardQuery.Sources, gathering.Item.RowId));
Check(gatheringSources.Valid && gatheringSources.Sources.Any(s => s.Kind == ItemSourceKind.Gathering && s.Location?.TerritoryId > 0), "Installed gathering relationships resolve an item and territory");
var shopItem = manager.GetSubrowExcelSheet<GilShopItem>().SelectMany(s => s).First(r => r.Item.RowId > 0 &&
    catalog.Read(Request(CardQuery.Sources, r.Item.RowId)).Sources.Any(s => s.Kind == ItemSourceKind.GilShop && s.NpcId > 0));
var shops = catalog.Read(Request(CardQuery.Sources, shopItem.Item.RowId));
Check(shops.Valid && shops.Sources.Any(s => s.GilPrice > 0 && s.NpcId > 0), "Installed gil shop resolves an NPC and listed purchase price");
Console.WriteLine($"INFO samples: weapon={weapon.RowId}; recipe={recipe.RowId}; gathering item={gathering.Item.RowId}; shop item={shopItem.Item.RowId}");
var stale = Request(CardQuery.Recipes, recipe.ItemResult.RowId); stale.DataScope = "previous-data-scope";
Check(catalog.Read(stale).Status == CardStatus.Stale, "Requests cannot combine pages across a changed data scope");
foreach (var result in new[] { card, recipes, gatheringSources, shops }) Check(result.Encode().Length <= CardProtocol.MaxPacketBytes, "Real sheet reply fits packet budget");
Console.WriteLine("All installed-game data checks completed");
return 0;
