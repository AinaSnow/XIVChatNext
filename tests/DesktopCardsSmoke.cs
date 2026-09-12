using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XIVChatCommon;
using XIVChatCommon.Message;

namespace XIVChat_Desktop {
    internal static class DesktopCardsSmokeProgram {
        [STAThread] public static void Main() {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(parameters => {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new DesktopCardsSmokeApp();
            });
        }
    }
    internal sealed partial class DesktopCardsSmokeApp : App {
        private readonly List<string> results = new();
        private void Check(bool condition, string label) { if (!condition) throw new Exception(label); this.results.Add("PASS " + label); }
        private static WebView2 Web(Window window, string name) => (WebView2)((FrameworkElement)window.Content).FindName(name);
        private static async Task Wait(WebView2 view, string expression) {
            for (int i = 0; i < 150; i++) {
                if (view.CoreWebView2 != null && await view.CoreWebView2.ExecuteScriptAsync(expression) == "true") return;
                await Task.Delay(100);
            }
            throw new Exception("WebView condition timed out: " + expression + " HTML=" + (view.CoreWebView2 == null ? "not initialized" : await view.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML")));
        }
        protected override async void OnLaunched(LaunchActivatedEventArgs args) {
            try {
                typeof(App).GetProperty(nameof(Config))!.SetValue(this, new Configuration { OnlineAvatars = false });
                ConfigureChineseFixture();
                LocalizationHelper.Initialize(AppLanguage.English);
                var item = new ItemWindow(); item.Activate();
                var web = Web(item, "ItemWebView");
                await App.EnsureWebView2Async(web);
                var chunk = new TextChunk("Example") {
                    ItemId = 123, ItemKind = 1_000_000, ItemName = "Test <gear>", ItemDescription = "Line 1\n<em id='injected'>literal markup</em>",
                    ItemEquipLevel = 100, ItemLevel = 700, ItemCategory = "Armor", ItemMateriaSlots = 2,
                    ItemStats = new() { "WRONG LEGACY VALUE" },
                    ItemDetails = new() { CanBeHq = true, EquipSlotCategoryId = 4, ClassJobs = "PLD WAR DRK GNB", Parameters = new() {
                        new ItemParameter { Id = 21, Name = "Defense", NqValue = 32, HqDelta = 3 },
                        new ItemParameter { Id = 24, Name = "Magic Defense", NqValue = 32, HqDelta = 3 },
                    } },
                    DataSource = new() { Language = "English", Version = "fixture-version", RetrievedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                };
                item.UpdateItem(123, true, "Example", chunk);
                try { await Wait(web, "document.body.textContent.includes('fixture-version')"); }
                catch (Exception ex) { throw new Exception(Control<TextBlock>(item, "CardStatusText").Text, ex); }
                string text = await web.CoreWebView2.ExecuteScriptAsync("document.body.innerText");
                Check(text.Contains("Defense +35") && text.Contains("Magic Defense +35") && !text.Contains("WRONG LEGACY"), "Structured HQ totals override legacy strings and preserve both defenses");
                Check(await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('injected') === null && document.body.innerText.includes('literal markup')") == "true", "Game descriptions render as text without HTML interpretation");
                Check(await web.CoreWebView2.ExecuteScriptAsync("document.body.textContent.includes('fixture-version') && document.body.textContent.includes('English')") == "true" && text.Contains("Base item attributes"), "Card retains provenance and base-stat scope");
                LocalizationHelper.ApplyLanguage(AppLanguage.ChineseSimplified);
                await Wait(web, "document.body.textContent.includes('游戏文件') && document.querySelector('h1').innerText.includes('中文测试戒指')");
                Check(true, "Open card relocalizes while retaining game-data language");
                using (var capture = File.Create(Path.Combine(AppContext.BaseDirectory, "cards-item-preview.png")))
                    await web.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, capture.AsRandomAccessStream());
                chunk.ItemKind = 500_000; chunk.ItemName = "Collectible fixture";
                item.UpdateItem(123, true, "Example", chunk);
                await Wait(web, "document.body.innerText.includes('收藏品')");
                Check(await web.CoreWebView2.ExecuteScriptAsync("!document.body.innerText.includes('HQ') && document.body.innerText.includes('物理防御 +32') && document.body.innerText.includes('魔法防御 +32')") == "true", "Collectible kind overrides HQ boolean and excludes HQ bonuses");
                item.UpdateItem(2_000_123, true, "Key item", new TextChunk("key") { ItemKind = 2_000_000, ItemName = "Key fixture" });
                await Wait(web, "document.body.innerText.includes('任务物品')");
                Check(await web.CoreWebView2.ExecuteScriptAsync("!document.body.innerText.includes('HQ')") == "true", "Key-item card does not display HQ badge");
                chineseFixture.PendingText = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                item.UpdateItem(9999, false, "日本語の装備", new TextChunk("日本語の装備") { ItemName = "日本語の装備", ItemCategory = "指輪", ItemDescription = "日本語の説明" });
                await Wait(web, "document.querySelector('h1')?.innerText.includes('正在加载卡片文本')===true");
                Check(await web.CoreWebView2.ExecuteScriptAsync("!document.body.innerText.includes('日本語')") == "true" && !Control<Button>(item, "CardCopy").IsEnabled, "Slow translation shows a stable loading state instead of a partially translated card");
                chineseFixture.PendingText.SetResult(); chineseFixture.PendingText = null;
                await Wait(web, "document.querySelector('h1')?.innerText.includes('中文资料 9999')===true");
                Check(await web.CoreWebView2.ExecuteScriptAsync("document.body.innerText.includes('指輪（暂无中文）') && document.body.innerText.includes('中文说明') && !document.body.innerText.includes('日本語の説明')") == "true", "Japanese fallback is explicitly marked beside translated Chinese text");
                var map = new MapWindow(); map.Activate();
                map.UpdateLocation(null, 10, 20, "Unknown <map>");
                var mapWeb = Web(map, "MapWebView");
                await Wait(mapWeb, "document.body.innerText.includes('缺少完整地图数据')");
                Check(await mapWeb.CoreWebView2.ExecuteScriptAsync("document.querySelector('.pin') === null && document.querySelector('a') === null") == "true", "Missing map ID does not guess a map or draw a marker");
                map.UpdateLocation(123, 10, 20, "Old map", "test/01", null);
                await Wait(mapWeb, "document.querySelector('a')?.href.includes('id=123') === true");
                Check(await mapWeb.CoreWebView2.ExecuteScriptAsync("document.querySelector('.pin') === null") == "true", "Missing scale exposes an explicit external reference without assuming scale 100");
                await TestWorkflow(item);
                this.results.Add("All card checks completed");
                File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "cards-smoke-results.txt"), this.results);
                item.Close(); map.Close(); this.Exit();
            } catch (Exception ex) {
                this.results.Add("FAIL " + ex);
                File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "cards-smoke-results.txt"), this.results);
                Environment.Exit(1);
            }
        }
    }
}
