using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop {
    internal sealed class ItemCardState {
        internal TextChunk Item = new("");
        internal CardOrigin Origin = new("", "");
        internal ChineseTextRow? Chinese;
        internal bool ShowChinese;
        internal int TextOperations;
        internal bool TextLoading => this.ShowChinese && this.TextOperations > 0;
        internal bool RefreshChinese;
        internal bool EquipmentLoading;
        internal int EquipmentVersion;
        internal Dictionary<string, Dictionary<uint, string>> Names = new();
        internal ServerGameCard? Recipes;
        internal ServerGameCard? Sources;
        internal bool RecipesLoading;
        internal bool SourcesLoading;
        internal CardStatus RecipesStatus = CardStatus.Busy;
        internal CardStatus SourcesStatus = CardStatus.Busy;
        internal bool RecipesCached;
        internal bool SourcesCached;
        internal EquipmentSnapshot? Equipment;
        internal int? SelectedSlot;
        internal string? DataScope;
        internal bool Cached;
        internal string StatusKey = "Card.Loading";
        internal string IconUrl = "";
        internal string Localized(string? translated, string original) => !this.ShowChinese ? original :
            !string.IsNullOrWhiteSpace(translated) ? translated : string.IsNullOrWhiteSpace(original) ? "" :
            original + "（" + LocalizationHelper.GetString("Card.NoChinese") + "）";
        internal string Name => this.Localized(this.Chinese?.Name, this.Item.ItemName ?? this.Item.Content);
        internal string Text(string table, uint id, string original) => this.Localized(
            this.Names.TryGetValue(table, out var names) && names.TryGetValue(id, out var value) ? value : null, original);
        internal string Parameter(uint id, string original) => this.Chinese?.Parameters.TryGetValue(id, out var value) == true
            ? this.Localized(value, original) : this.Text("BaseParam", id, original);
        internal string MapName(CardMap map) => this.Text("PlaceName", map.PlaceNameId, this.Text("Map", map.Id, map.Name));
    }
    internal static class GameCardHtml {
        private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");
        private static string L(string key) => LocalizationHelper.GetString(key);
        private static string Time(long milliseconds) => milliseconds is > 0 and < 253402300800000 ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToLocalTime().ToString("g") : "";
        private static string Button(string action, string label, bool disabled = false) => $"<button {(disabled ? "disabled" : "")} onclick=\"send('{action}')\">{E(label)}</button>";
        private static string PageButtons(string action, ServerGameCard page, bool loading) => "<div class='pages'>" +
            Button(action + ":" + (page.Page - 1), L("Card.Previous"), page.Page == 0 || loading) +
            $"<span>{page.Page + 1} / {page.PageCount}</span>" + Button(action + ":" + (page.Page + 1), L("Card.Next"), page.Page + 1 >= page.PageCount || loading) + "</div>";
        internal static string Copy(ItemCardState state) {
            var item = state.Item; bool hq = item.ItemKind == (uint)GameItemKind.Hq;
            return state.Name + (hq ? " HQ" : "") + $" (#{item.ItemId})\n" +
                string.Join("\n", item.ItemDetails?.Parameters.Select(p => state.Parameter(p.Id, p.Name) + " " + p.Value(hq)) ?? item.ItemStats?.Select(s => state.Localized(null, s)) ?? Enumerable.Empty<string>());
        }
        internal static string Build(ItemCardState state) {
            var item = state.Item; var details = item.ItemDetails;
            bool hq = item.ItemKind == (uint)GameItemKind.Hq;
            string kind = item.ItemKind switch { 500_000 => L("Item.Collectible"), 1_000_000 => "HQ", 2_000_000 => L("Item.EventItem"), _ => "" };
            string category = state.Localized(state.Chinese?.Category, item.ItemCategory ?? "");
            string jobs = state.Localized(state.Chinese?.ClassJobs, details?.ClassJobs ?? "");
            string description = state.Localized(state.Chinese?.Description, item.ItemDescription ?? "");
            string rarity = item.ItemRarity switch { 2 => "#8ce68c", 3 => "#89b4ff", 4 => "#cd9bff", 7 => "#ff9bc9", _ => "#f3dc8c" };
            var body = new StringBuilder();
            if (state.TextLoading) body.Append($"<header><h1>{E(L("Card.TextLoading"))}</h1></header><section class='muted' role='status'>{E(L("Card.Loading"))}</section>");
            else {
            body.Append($"<header>{(state.IconUrl.Length > 0 ? $"<img alt='' src='{E(state.IconUrl)}' onerror='this.style.display=\"none\"'/>" : "")}<div><h1 style='color:{rarity}'>{E(state.Name)} {(kind.Length > 0 ? $"<span class='badge'>{E(kind)}</span>" : "")}</h1><div class='muted'>{E(category)} · #{item.ItemId}</div>");
            if (item.ItemLevel > 0) body.Append($"<div class='muted'>{E(L("Item.Level"))} {item.ItemLevel} · {E(L("Item.EquipLevel"))} {item.ItemEquipLevel}</div>");
            body.Append("</div></header>");
            var stats = details?.Parameters.Where(p => p.Value(hq) != 0).Select(p => state.Parameter(p.Id, p.Name) + $" {p.Value(hq):+0;-0;0}").ToArray() ?? item.ItemStats?.Select(s => state.Localized(null, s)).ToArray() ?? Array.Empty<string>();
            if (stats.Length > 0) body.Append($"<section><h2>{E(L("Item.Stats"))}</h2>" + string.Join("", stats.Select(s => $"<div class='stat'>• {E(s)}</div>")) + "</section>");
            if (details?.EquipSlots.Length > 0 || item.ItemEquipLevel > 1) {
                body.Append($"<section><div>{E(jobs)}</div><div class='muted'>{E(string.Join(" / ", details?.EquipSlots.Select(s => L("Card.Slot." + s)) ?? Array.Empty<string>()))}</div>");
                body.Append($"<p>{E(L("Item.MateriaSlots"))}: {item.ItemMateriaSlots ?? 0} {(item.ItemIsAdvancedMeldingPermitted == true ? " · " + E(L("Item.AdvancedMelding")) : "")}</p><div class='muted'>{E(L("Item.BaseStatsOnly"))}</div></section>");
            }
            if (description.Length > 0) body.Append($"<section class='description'>{E(description)}</section>");
            if (stats.Length == 0 && description.Length == 0) body.Append($"<section class='muted'>{E(L("Item.NoDetails"))}</section>");
            }
            if (details?.EquipSlots.Length > 0) AppendComparison(body, state);
            if (item.ItemKind != (uint)GameItemKind.EventItem) { AppendRecipes(body, state); AppendSources(body, state); }
            body.Append($"<footer><details id='card-provenance'><summary>{E(L("Card.DataDetails"))}</summary><div class='provenance'>");
            if (item.DataSource is { } source) body.Append($"{E(L("Item.GameDataSource"))} · {E(source.Language)} · {E(source.Version)}<br>{E(Time(source.RetrievedAtUnixMilliseconds))}");
            if (state.ShowChinese && state.Chinese is { } chinese) body.Append($"<p>{E(L("Card.ChineseSource"))} · {E(chinese.Version)}<br>{E(Time(chinese.CapturedAt))}</p>");
            body.Append("</div></details></footer>");
            return "<!DOCTYPE html><html lang='" + E(LocalizationHelper.LanguageCode) + "'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><style>" +
                "*{box-sizing:border-box}body{margin:0;background:#12141a;color:#e5eaf2;font:14px 'Segoe UI','Microsoft YaHei',sans-serif;line-height:1.55}main{max-width:850px;margin:16px auto;border:1px solid #303847;border-radius:12px;overflow:hidden;background:#1a1f28}header{display:flex;align-items:center;gap:14px;padding:22px;background:#222936}header img{width:48px;height:48px;object-fit:contain}h1{font-size:21px;margin:0;line-height:1.4;overflow-wrap:anywhere}h2{font-size:15px;margin:0 0 12px;color:#d3deed}section{padding:18px 22px;border-top:1px solid #303847}.muted,footer{color:#a4b0c1;font-size:12px}.badge{display:inline-block;font-size:11px;background:#ffe180;color:#242017;border-radius:5px;padding:1px 6px;vertical-align:middle}.stat{padding:2px 0}.description{white-space:pre-wrap}.entry{padding:12px;border:1px solid #354051;border-radius:8px;margin:8px 0;background:#1e2631}.materials{display:flex;flex-wrap:wrap;gap:7px;margin-top:10px}button,select{font:inherit;border:1px solid #4c5d75;border-radius:6px;background:#26364a;color:#cbdfff;padding:6px 10px;cursor:pointer}button:hover{background:#344b66}button:disabled{opacity:.4;cursor:default}select{max-width:100%}.pages{display:flex;align-items:center;justify-content:space-between;gap:10px;margin-top:12px}table{border-collapse:collapse;width:100%;table-layout:fixed;margin:10px 0}th,td{text-align:right;padding:6px;border-bottom:1px solid #354051;overflow-wrap:anywhere}th:first-child,td:first-child{text-align:left}th{font-size:12px;color:#adbdd2}.positive{color:#8be4b0}.negative{color:#ffacac}footer{padding:18px 22px;border-top:1px solid #303847}p{margin:8px 0}.note{border-left:3px solid #d2b86e;padding-left:10px;margin:12px 0;color:#dccb9f;font-size:12px}@media(max-width:600px){main{margin:0;border-radius:0;border-left:0;border-right:0}section,header,footer{padding:16px}th,td{padding:5px;font-size:12px}}" +
                "summary{cursor:pointer;color:#cbdfff}.provenance{padding-top:10px}select{width:100%}" +
                "</style></head><body><main>" + body + "</main><script>function send(action){window.chrome.webview.postMessage(action)}</script></body></html>";
        }
        private static void AppendRecipes(StringBuilder body, ItemCardState state) {
            body.Append($"<section><h2>{E(L("Card.Recipes"))}</h2>");
            var page = state.Recipes;
            if (state.RecipesLoading || state.TextLoading) body.Append($"<div class='muted'>{E(L("Card.Loading"))}</div>");
            else if (page == null) body.Append($"<div class='muted'>{E(L("Card.DataUnavailable"))}</div>");
            else if (page.Recipes.Length == 0) body.Append($"<div class='muted'>{E(L("Card.NoRecipes"))}</div>");
            if (page != null && !state.TextLoading) {
                foreach (var recipe in page.Recipes) {
                    body.Append($"<div class='entry'><strong>{E(state.Text("CraftType", recipe.CraftTypeId, recipe.CraftJob))} Lv.{recipe.Level} {new string('★', Math.Clamp(recipe.Stars, 0, 6))}</strong> · {E(L("Card.Yield"))} {recipe.Yield}");
                    if (recipe.RequiresUnlock) body.Append($"<div class='muted'>{E(L("Card.RequiresUnlock"))}</div>");
                    body.Append("<div class='materials'>");
                    foreach (var ingredient in recipe.Ingredients) body.Append(Button("item:" + ingredient.Item.Id, state.Text("Item", ingredient.Item.Id, ingredient.Item.Name) + " ×" + ingredient.Quantity));
                    body.Append("</div></div>");
                }
                if (page.PageCount > 1) body.Append(PageButtons("recipes", page, state.RecipesLoading));
                if (page.Truncated) body.Append($"<p class='muted'>{E(L("Card.Truncated"))}</p>");
                if (state.RecipesCached) body.Append($"<p class='muted'>{E(L("Card.Cached"))} · {E(Time(page.Source?.RetrievedAtUnixMilliseconds ?? 0))}</p>");
            }
            body.Append("</section>");
        }
        private static void AppendSources(StringBuilder body, ItemCardState state) {
            body.Append($"<section><h2>{E(L("Card.Acquisition"))}</h2><div class='muted'>{E(L("Card.SourceScope"))}</div>");
            var page = state.Sources;
            if (state.SourcesLoading || state.TextLoading) body.Append($"<p class='muted'>{E(L("Card.Loading"))}</p>");
            else if (page == null) body.Append($"<p class='muted'>{E(L("Card.DataUnavailable"))}</p>");
            else if (page.Sources.Length == 0) body.Append($"<p class='muted'>{E(L("Card.NoSources"))}</p>");
            if (page != null && !state.TextLoading) {
                for (int index = 0; index < page.Sources.Length; index++) {
                    var source = page.Sources[index];
                    string name = source.Kind == ItemSourceKind.GilShop ? state.Text("GilShop", source.Id, source.Name) : state.Text("GatheringType", source.GatheringTypeId, source.Name);
                    body.Append($"<div class='entry'><strong>{E(source.Kind == ItemSourceKind.GilShop ? L("Card.GilShop") : L("Card.Gathering"))}</strong> · {E(name)}");
                    if (source.Kind == ItemSourceKind.GilShop) {
                        body.Append($"<p>{E(source.NpcId > 0 ? state.Text("ENpcResident", source.NpcId, source.NpcName) : L("Card.NpcUnknown"))}</p><p>{E(L("Card.ListPrice"))}: {(source.GilPrice.HasValue ? source.GilPrice + " gil" : E(L("Card.Unknown")))} · {(source.ItemKind == 1_000_000 ? "HQ" : "NQ")}</p>");
                    } else body.Append($"<p>Lv.{source.GatheringLevel}</p>");
                    if (source.Location is { } location) {
                        body.Append($"<p>{E(state.MapName(location))}</p>");
                        if (location.Id > 0 && location.X.HasValue && location.Y.HasValue)
                            body.Append(Button("map:" + index, FormattableString.Invariant($"{L("Card.OpenMap")} ({location.X:0.0}, {location.Y:0.0})")));
                        else body.Append($"<div class='muted'>{E(L("Card.PositionUnknown"))}</div>");
                    } else body.Append($"<div class='muted'>{E(L("Card.PositionUnknown"))}</div>");
                    if (source.RequiresUnlock) body.Append($"<div class='note'>{E(L("Card.RequiresUnlock"))}</div>");
                    if (source.TimedOrHidden) body.Append($"<div class='note'>{E(L("Card.Timed"))}</div>");
                    body.Append("</div>");
                }
                if (page.PageCount > 1) body.Append(PageButtons("sources", page, state.SourcesLoading));
                if (page.Truncated) body.Append($"<p class='muted'>{E(L("Card.Truncated"))}</p>");
                if (state.SourcesCached) body.Append($"<p class='muted'>{E(L("Card.Cached"))} · {E(Time(page.Source?.RetrievedAtUnixMilliseconds ?? 0))}</p>");
            }
            body.Append("</section>");
        }
        private static void AppendComparison(StringBuilder body, ItemCardState state) {
            body.Append($"<section><h2>{E(L("Card.Comparison"))}</h2>");
            var snapshot = state.Equipment;
            if (state.TextLoading || state.EquipmentLoading && snapshot == null) { body.Append($"<p class='muted' role='status'>{E(L("Card.TextLoading"))}</p></section>"); return; }
            if (snapshot == null || snapshot.Items.Length == 0) { body.Append($"<p class='muted'>{E(L("Card.NoEquipment"))}</p></section>"); return; }
            var candidate = state.Item;
            var selected = snapshot.Items.FirstOrDefault(i => i.Slot == state.SelectedSlot) ?? snapshot.Items.FirstOrDefault(i => candidate.ItemDetails!.EquipSlots.Contains(i.Slot)) ?? snapshot.Items[0];
            body.Append($"<div class='muted'>{E(L(snapshot.IsLive ? "Card.EquipmentLive" : "Card.EquipmentCached"))} · {E(Time(snapshot.CapturedAtUnixMilliseconds))}</div><select aria-label='{E(L("Card.CompareSlot"))}' onchange=\"send('compare:'+this.value)\">");
            foreach (var item in snapshot.Items) body.Append($"<option value='{item.Slot}' {(item.Slot == selected.Slot ? "selected" : "")}>{E(L("Card.Slot." + item.Slot))} · {E(state.Text("Item", item.Item.ItemId ?? 0, item.Item.ItemName ?? ""))}</option>");
            body.Append("</select>");
            var compared = GearComparer.Compare(candidate, snapshot, selected); bool canCompare = compared.Reason == GearComparisonReason.Comparable;
            if (!canCompare) body.Append($"<p class='note'>{E(L("Card.Compare." + compared.Reason))}</p>");
            if (snapshot.IsLevelSynced) body.Append($"<p class='note'>{E(L("Card.SyncNote"))}</p>");
            var before = selected.Item.ItemDetails?.Parameters ?? new(); var after = candidate.ItemDetails?.Parameters ?? new();
            body.Append($"<table><thead><tr><th>{E(L("Item.Stats"))}</th><th>{E(L("Card.Candidate"))}</th><th>{E(L("Card.Equipped"))}</th><th>{E(L("Card.Difference"))}</th></tr></thead><tbody>");
            foreach (var id in after.Select(p => p.Id).Union(before.Select(p => p.Id))) {
                var next = after.FirstOrDefault(p => p.Id == id); var old = before.FirstOrDefault(p => p.Id == id);
                int a = next?.Value(candidate.ItemKind == 1_000_000) ?? 0; int b = old?.Value(selected.Item.ItemKind == 1_000_000) ?? 0;
                body.Append($"<tr><td>{E(state.Parameter(id, next?.Name ?? old!.Name))}</td><td>{a}</td><td>{b}</td><td class='{(canCompare && a > b ? "positive" : canCompare && a < b ? "negative" : "")}'>{(canCompare ? (a - b).ToString("+0;-0;0", CultureInfo.InvariantCulture) : "—")}</td></tr>");
            }
            body.Append($"</tbody></table><h2>{E(L("Card.ActualMateria"))}</h2>");
            if (selected.Materia.Length == 0) body.Append($"<p class='muted'>{E(L(selected.HasCustomStats ? "Card.CustomStats" : "Card.NoMateria"))}</p>");
            foreach (var materia in selected.Materia) body.Append($"<div class='stat'>{Button("materia:" + materia.Item.Id, state.Text("Item", materia.Item.Id, materia.Item.Name))} · {E(state.Parameter(materia.ParameterId, materia.ParameterName))} +{materia.Value}</div>");
            body.Append($"<p class='muted'>{E(L("Card.MateriaNote"))}</p></section>");
        }
    }
}
