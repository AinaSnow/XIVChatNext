using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using XIVChatCommon;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace XIVChat_Desktop {
    public sealed partial class ItemWindow : Window {
        private static ItemWindow? _instance;
        private Action? refreshLocalizedItem;
        private int renderVersion;

        public ItemWindow() {
            this.InitializeComponent();
            Branding.ApplyWindowIcon(this);
            Localize.BindWindow(this, () => { if (refreshLocalizedItem != null) refreshLocalizedItem(); else this.Title = LocalizationHelper.GetString("Item.Title"); });
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(480, 620));
            this.Closed += ItemWindow_Closed;
        }

        private void ItemWindow_Closed(object sender, WindowEventArgs args) {
            this.renderVersion++;
            this.refreshLocalizedItem = null;
            _instance = null;
        }

        public static void ShowItem(uint? itemId, bool isHq, string? itemName, XIVChatCommon.Message.TextChunk? chunk = null) {
            if (_instance == null) {
                _instance = new ItemWindow();
            }

            _instance.Activate();
            _instance.UpdateItem(itemId, isHq, itemName, chunk);
        }

        public async void UpdateItem(uint? itemId, bool isHq, string? itemName, XIVChatCommon.Message.TextChunk? chunk = null) {
            int request = ++this.renderVersion;
            var originalId = itemId;
            var originalHq = isHq;
            var identity = GameItemIdentity.Resolve(itemId ?? 0, chunk?.ItemKind, isHq);
            itemId = identity.Id;
            isHq = identity.Kind == GameItemKind.Hq;
            static string Encode(string? value) => WebUtility.HtmlEncode(value ?? "");
            refreshLocalizedItem = () => UpdateItem(originalId, originalHq, itemName, chunk);
            string cleanName = itemName?.Trim() ?? "";
            cleanName = Regex.Replace(cleanName, @"^[\uE000-\uF8FF\[（【(]+|[\]）】)]+$", "").Trim();

            this.AppWindow.Title = !string.IsNullOrEmpty(cleanName) ? $"XIVChat - {cleanName}" : LocalizationHelper.GetString("Item.Title");

            string htmlContent;
            if (chunk != null && (!string.IsNullOrEmpty(chunk.ItemDescription) || !string.IsNullOrEmpty(chunk.ItemName))) {
                string rarityColor = chunk.ItemRarity switch {
                    1 => "#f0f0f0", // 白装/普通物品
                    2 => "#8ce68c", // 绿装
                    3 => "#5990ff", // 蓝装
                    4 => "#be73ff", // 紫装
                    7 => "#ff73be", // 粉装
                    _ => "#ffd700"  // 默认金色
                };
                string displayName = !string.IsNullOrEmpty(chunk.ItemName) ? chunk.ItemName : (!string.IsNullOrEmpty(cleanName) ? cleanName : $"{LocalizationHelper.GetString("Item.Category")} #{itemId}");
                displayName = Encode(displayName);
                string hqBadge = isHq ? @"<span class=""hq-badge""><svg width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""#111"" style=""margin-right:2px; vertical-align:-1px;""><path d=""M12 2L14.4 9.6L22 12L14.4 14.4L12 22L9.6 14.4L2 12L9.6 9.6L12 2Z""/></svg>HQ</span>" : "";
                string categoryText = !string.IsNullOrEmpty(chunk.ItemCategory) ? chunk.ItemCategory : LocalizationHelper.GetString("Item.Category");
                categoryText = Encode(categoryText);
                if (identity.Kind is GameItemKind.Collectible or GameItemKind.EventItem)
                    hqBadge = $"<span class=\"hq-badge\">{Encode(LocalizationHelper.GetString(identity.Kind == GameItemKind.Collectible ? "Item.Collectible" : "Item.EventItem"))}</span>";
                string levelText = chunk.ItemLevel.HasValue && chunk.ItemLevel.Value > 0 ? $" | {LocalizationHelper.GetString("Item.Level")} {chunk.ItemLevel}" : "";
                string equipLevelText = chunk.ItemEquipLevel.HasValue && chunk.ItemEquipLevel.Value > 0 ? $" ({LocalizationHelper.GetString("Item.EquipLevel")} {chunk.ItemEquipLevel})" : "";
                string descHtml = !string.IsNullOrEmpty(chunk.ItemDescription)
                    ? $"<div class=\"description\">{Encode(chunk.ItemDescription)}</div>"
                    : ((chunk.ItemStats != null && chunk.ItemStats.Count > 0) || (chunk.ItemMateriaSlots.HasValue && chunk.ItemMateriaSlots.Value > 0) ? "" : $"<div class=\"description\">{LocalizationHelper.GetString("Item.NoDescription")}</div>");

                string iconImgHtml = "";
                if (chunk.ItemIconId.HasValue && chunk.ItemIconId.Value > 0) {
                    string iconStr = chunk.ItemIconId.Value.ToString("D6");
                    string folderStr = (chunk.ItemIconId.Value / 1000 * 1000).ToString("D6");
                    string iconUrl = $"https://cafemaker.wakingsands.com/i/{folderStr}/{iconStr}.png";
                    string xivapiUrl = $"https://xivapi.com/i/{folderStr}/{iconStr}.png";
                    string ghUrl = $"https://raw.githubusercontent.com/xivapi/ffxiv-datamining/master/icons/{folderStr}/{iconStr}.png";
                    string cachedIconUrl = await LocalAssetCache.GetCachedImageAsync($"icons/{folderStr}", $"{iconStr}.png", iconUrl, xivapiUrl, ghUrl);
                    if (request != this.renderVersion) return;
                    string hqOverlayHtml = isHq ? "<div class=\"hq-overlay\">HQ</div>" : "";
                    string onErrorJs = $"if (this.src.indexOf('cafemaker') !== -1) {{ this.src='{xivapiUrl}'; }} else if (this.src.indexOf('xivapi.com') !== -1) {{ this.src='{ghUrl}'; }} else {{ this.parentNode.style.display='none'; }}";
                    iconImgHtml = $"<div class=\"icon-container\"><img src=\"{cachedIconUrl}\" class=\"icon\" onerror=\"{onErrorJs}\" />{hqOverlayHtml}</div>";
                }

                string statsHtml = "";
                var displayStats = chunk.ItemDetails != null
                    ? chunk.ItemDetails.Parameters.Where(p => p.Value(isHq) != 0).Select(p => $"{p.Name} {p.Value(isHq):+0;-0;0}").ToList()
                    : chunk.ItemStats;
                if (displayStats != null && displayStats.Count > 0) {
                    string statsItems = "";
                    foreach (var stat in displayStats) {
                        statsItems += $"<div class=\"stat-row\">• {Encode(stat)}</div>";
                    }
                    statsHtml = $"<div class=\"stats-box\"><div class=\"stats-title\">{LocalizationHelper.GetString("Item.Stats")}</div>{statsItems}</div>";
                }

                string sourceHtml = "";
                if (chunk.ItemDetails?.EquipSlotCategoryId > 0) {
                    sourceHtml += $"<div class=\"description\">{Encode(chunk.ItemDetails.ClassJobs)}<br>{Encode(LocalizationHelper.GetString("Item.BaseStatsOnly"))}</div>";
                }
                if (chunk.DataSource is { Provider: "game-files" } source) {
                    string captured = source.RetrievedAtUnixMilliseconds is > 0 and < 253402300800000
                        ? DateTimeOffset.FromUnixTimeMilliseconds(source.RetrievedAtUnixMilliseconds).ToLocalTime().ToString("g") : "";
                    sourceHtml += $"<div class=\"description\" style=\"font-size:12px;color:#8c96ab\">{Encode(LocalizationHelper.GetString("Item.GameDataSource"))} · {Encode(source.Language)} · {Encode(source.Version)}<br>{Encode(captured)}</div>";
                }

                string materiaHtml = "";
                if (chunk.ItemMateriaSlots.HasValue && chunk.ItemEquipLevel.HasValue && chunk.ItemEquipLevel.Value > 0) {
                    var circles = new List<string>();
                    for (int i = 0; i < chunk.ItemMateriaSlots.Value; i++) {
                        circles.Add("🟢");
                    }
                    string advMeldingText = chunk.ItemIsAdvancedMeldingPermitted == true ? $" <span style=\"color:#aaa;\">({LocalizationHelper.GetString("Item.AdvancedMelding")})</span>" : "";
                    if (circles.Count > 0 || chunk.ItemIsAdvancedMeldingPermitted == true) {
                        string slotsStr = circles.Count > 0 ? string.Join(" ", circles) : LocalizationHelper.GetString("Item.NoSlots");
                        materiaHtml = $"<div class=\"materia-area\">{LocalizationHelper.GetString("Item.MateriaSlots")}: {slotsStr}{advMeldingText}</div>";
                    }
                }

                htmlContent = $@"<!DOCTYPE html>
<html lang=""{LocalizationHelper.LanguageCode}"">
<head>
  <meta charset=""UTF-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <style>
    body {{
      margin: 0;
      padding: 24px 20px;
      background-color: #12141a;
      color: #eeeeee;
      font-family: 'Microsoft YaHei', -apple-system, sans-serif;
      user-select: none;
    }}
    .container {{
      max-width: 440px;
      margin: 0 auto;
      background: #191c24;
      border: 1px solid #2d3240;
      border-radius: 10px;
      box-shadow: 0 8px 24px rgba(0,0,0,0.6);
      overflow: hidden;
    }}
    .header {{
      display: flex;
      align-items: center;
      padding: 16px;
      background: #20242e;
      border-bottom: 1px solid #2d3240;
    }}
    .icon-container {{
      position: relative;
      width: 52px;
      height: 52px;
      margin-right: 14px;
      flex-shrink: 0;
      background: #0f1117;
      border: 1px solid #3d4454;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
    }}
    .icon {{
      max-width: 44px;
      max-height: 44px;
      border-radius: 4px;
    }}
    .hq-overlay {{
      position: absolute;
      bottom: -4px;
      right: -4px;
      background: linear-gradient(135deg, #ffe866, #ffb800);
      color: #000;
      font-size: 10px;
      font-weight: 900;
      padding: 1px 4px;
      border-radius: 4px;
      box-shadow: 0 2px 4px rgba(0,0,0,0.8);
    }}
    .title-area {{
      flex-grow: 1;
    }}
    .item-name {{
      font-size: 18px;
      font-weight: bold;
      color: {rarityColor};
      display: flex;
      align-items: center;
      line-height: 1.3;
    }}
    .hq-badge {{
      display: inline-flex;
      align-items: center;
      background: linear-gradient(135deg, #ffe866, #ffb800);
      color: #000;
      font-size: 11px;
      font-weight: 900;
      padding: 1px 6px;
      border-radius: 4px;
      margin-left: 8px;
      vertical-align: middle;
    }}
    .item-meta {{
      font-size: 13px;
      color: #8c96ab;
      margin-top: 4px;
    }}
    .stats-box {{
      padding: 14px 16px;
      border-bottom: 1px solid #2d3240;
      background: rgba(255,255,255,0.015);
    }}
    .stats-title {{
      font-size: 12px;
      color: #8c96ab;
      text-transform: uppercase;
      letter-spacing: 1px;
      margin-bottom: 8px;
      font-weight: bold;
    }}
    .stat-row {{
      font-size: 14px;
      color: #e2e8f0;
      margin: 4px 0;
    }}
    .materia-area {{
      padding: 12px 16px;
      border-bottom: 1px solid #2d3240;
      font-size: 13px;
      color: #cbd5e1;
      background: #161820;
    }}
    .description {{
      padding: 16px;
      font-size: 14px;
      color: #cbd5e1;
      line-height: 1.6;
      white-space: pre-wrap;
    }}
  </style>
</head>
<body>
  <div class=""container"">
    <div class=""header"">
      {iconImgHtml}
      <div class=""title-area"">
        <div class=""item-name"">{displayName}{hqBadge}</div>
        <div class=""item-meta"">{categoryText}{levelText}{equipLevelText}</div>
      </div>
    </div>
    {statsHtml}
    {materiaHtml}
    {descHtml}
    {sourceHtml}
  </div>
</body>
</html>";
            } else {
                string displayName = !string.IsNullOrEmpty(cleanName) ? cleanName : $"{LocalizationHelper.GetString("Item.Category")} #{itemId}";
                displayName = Encode(displayName);
                htmlContent = $@"<!DOCTYPE html>
<html lang=""{LocalizationHelper.LanguageCode}"">
<head>
  <meta charset=""UTF-8"" />
  <style>
    body {{
      margin: 0;
      padding: 40px 20px;
      background-color: #12141a;
      color: #eeeeee;
      font-family: 'Microsoft YaHei', sans-serif;
      text-align: center;
    }}
  </style>
</head>
<body>
  <div style=""font-size: 16px; color: #cbd5e1;"">{displayName}</div>
  <div style=""font-size: 13px; color: #8c96ab; margin-top: 12px;"">{LocalizationHelper.GetString("Item.NoDetails")}</div>
</body>
</html>";
            }

            try {
                await App.EnsureWebView2Async(this.ItemWebView);
                if (request != this.renderVersion) return;
                try {
                    this.ItemWebView.CoreWebView2.SetVirtualHostNameToFolderMapping("cache.local", LocalAssetCache.CacheDir, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                } catch { }
                this.ItemWebView.NavigateToString(htmlContent);
            } catch (Exception ex) {
                Debug.WriteLine($"WebView2 load failed: {ex.Message}");
            }
        }
    }
}
