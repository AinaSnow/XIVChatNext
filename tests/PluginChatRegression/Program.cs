using System.Runtime.Loader;
using Dalamud.Game;
using Lumina;
using Lumina.Excel.Sheets;
using Dalamud.Utility;
using XIVChatCommon;
using XIVChatPlugin;

if (args.Length != 2) { Console.Error.WriteLine("Usage: PluginChatRegression <game/sqpack> <Dalamud libraries>"); return 2; }
AssemblyLoadContext.Default.Resolving += (context, name) => {
    var path = Path.Combine(Path.GetFullPath(args[1]), name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
try {
    Console.WriteLine("Client languages: " + string.Join(",", Enum.GetNames<ClientLanguage>()));
    Console.WriteLine("Sheet languages: " + string.Join(",", Enum.GetNames<Lumina.Data.Language>()));
    using var data = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    var language = Enum.TryParse<ClientLanguage>("ChineseSimplified", out var cn) ? cn : (ClientLanguage)4;
    var source = new GameSheetSource(data, language);
    var failures = new List<Exception>();
    var metadata = new LinkMetadataCache(source, failures.Add);
    var items = source.GetExcelSheet<Item>();
    foreach (var name in new[] { "陆行鸟充气床", "彩蛋小鸡眼镜" }) {
        var row = items.First(i => i.Name.ExtractText() == name);
        var card = metadata.ItemChunk(row.RowId, ItemKind.Normal);
        if (card == null || card.ItemName != name) throw new Exception("Missing CN item card: " + name + ": " + failures.FirstOrDefault());
        Console.WriteLine($"PASS CN linked item {row.RowId}: {card.ItemName}");
        if (row.EquipSlotCategory.RowId > 0 && card.ItemDetails!.AllowedJobs.Length == 0) throw new Exception("Empty equipment job restrictions");
    }
    var equipped = items.First(i => i.EquipSlotCategory.RowId > 0 && i.ClassJobCategory.RowId > 0 && i.LevelEquip > 0);
    var gear = metadata.ItemChunk(equipped.RowId, ItemKind.Normal);
    if (gear?.ItemDetails?.AllowedJobs.Length is not > 0 || failures.Count != 0) throw new Exception("CN equipped-item metadata failed: " + failures.FirstOrDefault());
    Console.WriteLine("PASS CN equipped-item restrictions use local sheets");
    // The old forced-English read fails against this same real CN installation.
    try { _ = data.Excel.GetSheet<ClassJob>(Lumina.Data.Language.English).Count; throw new Exception("Expected English sheet to be unavailable"); }
    catch (Lumina.Excel.Exceptions.UnsupportedLanguageException) { Console.WriteLine("PASS Reproduced former forced-English failure on CN data"); }
    var unavailable = new LinkMetadataCache(new GameSheetSource(data, ClientLanguage.English), failures.Add);
    var count = failures.Count;
    if (unavailable.Item(equipped.RowId, ItemKind.Normal) != null || failures.Count != count + 1) throw new Exception("Optional metadata failure must degrade to a missing card");
    if (unavailable.Item(equipped.RowId, ItemKind.Normal) != null || failures.Count != count + 1) throw new Exception("Failed metadata must be cached to avoid repeated exceptions");
    Console.WriteLine("PASS Unavailable metadata returns null and is cached");
    var raw = System.Text.Encoding.UTF8.GetBytes("前文 陆行鸟充气床 后文");
    var original = raw.ToArray();
    var fallbackErrors = new List<Exception>();
    var fallback = ChatChunkFallback.Convert(() => throw new InvalidOperationException("Synthetic metadata failure"), raw, 0x123456ff, fallbackErrors.Add);
    if (string.Concat(fallback.OfType<XIVChatCommon.Message.TextChunk>().Select(c => c.Content)) != System.Text.Encoding.UTF8.GetString(raw) || !original.SequenceEqual(raw) || fallbackErrors.Count != 1)
        throw new Exception("Chat fallback lost text or changed raw content");
    Console.WriteLine("PASS Failed enrichment preserves linked message text and original bytes");
    var following = ChatChunkFallback.Convert(() => new[] { new XIVChatCommon.Message.TextChunk("33") }, System.Text.Encoding.UTF8.GetBytes("33"), null, fallbackErrors.Add);
    if (following.OfType<XIVChatCommon.Message.TextChunk>().Single().Content != "33" || fallbackErrors.Count != 1) throw new Exception("Fallback affects subsequent messages");
    Console.WriteLine("PASS Following chat is unaffected");
    return 0;
} catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
