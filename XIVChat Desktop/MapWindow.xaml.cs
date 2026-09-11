using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace XIVChat_Desktop {
    public sealed partial class MapWindow : Window {
        private static MapWindow? _instance;
        private string? localizedPlaceName;
        private int renderVersion;

        public MapWindow() {
            this.InitializeComponent();
            Branding.ApplyWindowIcon(this);
            Localize.BindWindow(this, () => this.Title = string.IsNullOrEmpty(localizedPlaceName) ? LocalizationHelper.GetString("Map.Title") : $"XIVChat - {localizedPlaceName}");
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(950, 750));
            this.Closed += MapWindow_Closed;
        }

        private void MapWindow_Closed(object sender, WindowEventArgs args) {
            this.renderVersion++;
            _instance = null;
        }

        public static void ShowMap(uint? mapId, float? x, float? y, string? placeName, string? mapFilenameId = null, ushort? sizeFactor = null) {
            if (_instance == null) {
                _instance = new MapWindow();
            }

            _instance.Activate();
            _instance.UpdateLocation(mapId, x, y, placeName, mapFilenameId, sizeFactor);
        }

        public async void UpdateLocation(uint? mapId, float? x, float? y, string? placeName, string? mapFilenameId = null, ushort? sizeFactor = null) {
            int request = ++this.renderVersion;
            localizedPlaceName = placeName;
            var currentMapFilenameId = mapFilenameId;
            var currentMapSizeFactor = sizeFactor;

            if (mapId.HasValue && mapId.Value > 0) {
                if (string.IsNullOrEmpty(currentMapFilenameId) && Microsoft.UI.Xaml.Application.Current is App app && app.Window?.CurrentPlayerData?.mapId == mapId.Value) {
                    currentMapFilenameId = app.Window.CurrentPlayerData.mapFilenameId;
                    if (!currentMapSizeFactor.HasValue) currentMapSizeFactor = app.Window.CurrentPlayerData.mapSizeFactor;
                }
            }

            if ((!x.HasValue || x.Value <= 0) && !string.IsNullOrEmpty(placeName)) {
                var match = Regex.Match(placeName, @"[\(\[\{（【]?\s*([Xx]:?\s*)?([0-9]+(?:\.[0-9]+)?)\s*[,，]\s*([Yy]:?\s*)?([0-9]+(?:\.[0-9]+)?)\s*[\)\]\}）】]?");
                if (match.Success) {
                    if (float.TryParse(match.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out float mx) && float.TryParse(match.Groups[4].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out float my)) {
                        if (mx > 0 && my > 0) {
                            x = mx;
                            y = my;
                        }
                    }
                }
            }

            this.AppWindow.Title = !string.IsNullOrEmpty(placeName) ? $"XIVChat - {placeName}" : LocalizationHelper.GetString("Map.Title");

            uint parsedId = mapId ?? 0;
            float parsedX = x ?? 0f;
            float parsedY = y ?? 0f;

            string fallbackUrl;
            if (parsedId > 0 && parsedX > 0 && parsedY > 0) {
                fallbackUrl = FormattableString.Invariant($"https://map.wakingsands.com/#f=mark&id={parsedId}&x={parsedX:0.0}&y={parsedY:0.0}");
            } else if (parsedId > 0) {
                fallbackUrl = $"https://map.wakingsands.com/#f=mark&id={parsedId}";
            } else {
                fallbackUrl = "https://map.wakingsands.com/";
            }

            try {
                await App.EnsureWebView2Async(this.MapWebView);
                if (request != this.renderVersion) return;
                try {
                    this.MapWebView.CoreWebView2.SetVirtualHostNameToFolderMapping("cache.local", LocalAssetCache.CacheDir, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                } catch { }

                // Numeric map scale and filename must come from the same game record.
                if (parsedId > 0 && currentMapSizeFactor > 0 && currentMapFilenameId != null && Regex.IsMatch(currentMapFilenameId, @"\A[a-z0-9]+/[0-9]+\z")) {
                    var parts = currentMapFilenameId.Split('/');
                    string folder = parts[0];
                    string filename = $"{parts[0]}.{parts[1]}.jpg";
                    string xivapiUrl = $"https://xivapi.com/m/{folder}/{filename}";
                    string cafeUrl = $"https://cafemaker.wakingsands.com/m/{folder}/{filename}";
                    string cachedMapUrl = await LocalAssetCache.GetCachedImageAsync($"maps/{folder}", filename, xivapiUrl, cafeUrl);
                    if (request != this.renderVersion) return;

                    float sf = currentMapSizeFactor.Value;
                    string pinHtml = "";
                    if (parsedX > 0 && parsedY > 0) {
                        string percentX = ((parsedX - 1.0f) * (sf / 100.0f) / 41.0f * 100.0f).ToString("0.00", CultureInfo.InvariantCulture);
                        string percentY = ((parsedY - 1.0f) * (sf / 100.0f) / 41.0f * 100.0f).ToString("0.00", CultureInfo.InvariantCulture);
                        string coordStr = WebUtility.HtmlEncode(!string.IsNullOrEmpty(placeName) ? placeName : $"X: {parsedX:0.0}, Y: {parsedY:0.0}");
                        pinHtml = $@"
        <div class='coord-badge' style='left: {percentX}%; top: {percentY}%;'>{coordStr}</div>
        <div class='pin' style='left: {percentX}%; top: {percentY}%;'>
            <svg class='pin-icon' viewBox='0 0 24 24' fill='none' xmlns='http://www.w3.org/2000/svg'>
                <path d='M12 2C8.13 2 5 5.13 5 9C5 14.25 12 22 12 22C12 22 19 14.25 19 9C19 5.13 15.87 2 12 2ZM12 11.5C10.62 11.5 9.5 10.38 9.5 9C9.5 7.62 10.62 6.5 12 6.5C13.38 6.5 14.5 7.62 14.5 9C14.5 10.38 13.38 11.5 12 11.5Z' fill='#FF2D55' stroke='#FFFFFF' stroke-width='1.5'/>
            </svg>
        </div>";
                    }

                    string html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ margin: 0; background: #12141a; overflow: hidden; display: flex; justify-content: center; align-items: center; height: 100vh; font-family: sans-serif; user-select: none; }}
        .wrapper {{ width: 100vw; height: 100vh; display: flex; justify-content: center; align-items: center; }}
        .map-box {{ position: relative; width: min(96vw, 96vh); height: min(96vw, 96vh); border: 1px solid #333; border-radius: 8px; box-shadow: 0 0 30px rgba(0,0,0,0.85); background: #0c0d11; overflow: hidden; }}
        .map-img {{ width: 100%; height: 100%; display: block; }}
        .pin {{ position: absolute; transform: translate(-50%, -100%); z-index: 10; pointer-events: none; }}
        .pin-icon {{ width: 40px; height: 40px; filter: drop-shadow(0 3px 6px rgba(0,0,0,0.95)); animation: bounce 0.8s infinite alternate ease-in-out; }}
        @keyframes bounce {{ from {{ transform: translateY(0); }} to {{ transform: translateY(-8px); }} }}
        .coord-badge {{ position: absolute; transform: translate(-50%, 8px); background: rgba(18, 20, 26, 0.95); color: #ffd700; border: 1px solid #c9a338; padding: 4px 10px; border-radius: 4px; font-size: 13px; font-weight: bold; white-space: nowrap; box-shadow: 0 2px 8px rgba(0,0,0,0.85); z-index: 11; pointer-events: none; }}
    </style>
</head>
<body>
    <div class='wrapper'>
        <div class='map-box'>
            <img class='map-img' src='{cachedMapUrl}' onerror=""if(this.src.indexOf('xivapi')!==-1){{this.src='{cafeUrl}';}}"" />
            {pinHtml}
        </div>
    </div>
</body>
</html>";
                    this.MapWebView.CoreWebView2.NavigateToString(html);
                } else {
                    string label = WebUtility.HtmlEncode(placeName ?? LocalizationHelper.GetString("Map.Title"));
                    string missing = WebUtility.HtmlEncode(LocalizationHelper.GetString("Map.MissingMetadata"));
                    string external = parsedId > 0 ? $"<p><a style='color:#95bfff' href='{WebUtility.HtmlEncode(fallbackUrl)}'>{WebUtility.HtmlEncode(LocalizationHelper.GetString("Map.ExternalMap"))}</a></p>" : "";
                    this.MapWebView.CoreWebView2.NavigateToString($"<html><meta charset='utf-8'><body style='background:#12141a;color:#eee;font-family:sans-serif;padding:32px'><h3>{label}</h3><p>{missing}</p>{external}</body></html>");
                }
            } catch (Exception ex) {
                Debug.WriteLine($"WebView2 load failed: {ex.Message}");
            }
        }
    }
}
