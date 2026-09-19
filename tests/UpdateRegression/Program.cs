using System.Net;
using System.Text;
using System.Text.Json;
using XIVChat_Desktop;

internal static class Program {
    private const string Repo = "https://github.com/AinaSnow/XIVChatNext";
    private static int checks;
    private static void Check(bool value, string name) {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS " + name); checks++;
    }
    private static object Release(string tag, string? version = "1.4.2", bool draft = false, bool prerelease = false,
        string? download = null, string? page = null, string state = "uploaded") => new {
        tag_name = tag, draft, prerelease, html_url = page ?? Repo + "/releases/tag/" + tag, body = "中文更新说明\nRelease notes",
        assets = version == null ? Array.Empty<object>() : new object[] { new {
            name = "XIVChatNext-Desktop-v" + version + "-win-x64.zip", state,
            browser_download_url = download ?? Repo + "/releases/download/" + tag + "/XIVChatNext-Desktop-v" + version + "-win-x64.zip"
        } }
    };
    private static string Json(params object[] releases) => JsonSerializer.Serialize(releases);
    private static DesktopRelease? Parse(params object[] releases) => DesktopReleaseClient.ReadPage(Json(releases), out _);
    private static async Task<DesktopUpdates> RunResponse(HttpResponseMessage response) {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(response)));
        var session = new DesktopUpdates(new Version(1, 4, 1), new DesktopReleaseClient(http).FindLatestAsync);
        await session.CheckAsync(); return session;
    }
    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    public static async Task<int> Main(string[] args) {
        try {
            if (args.Contains("--live")) {
                var live = await new DesktopReleaseClient().FindLatestAsync(CancellationToken.None);
                Console.WriteLine($"Public desktop release: {live.Version}; {live.Download}");
                return 0;
            }
            var combined = Parse(Release("1.7.99", "1.4.2"))!;
            Check(combined.Version == new Version(1, 4, 2, 0), "Combined release compares desktop asset version, not plugin tag");
            Check(Parse(Release("1.7.100", null), Release("desktop-1.4.2"))?.Version == combined.Version, "Plugin-only release cannot hide a desktop update");
            Check(Parse(Release("preview", "9.0.0", prerelease: true), Release("draft", "10.0.0", draft: true), Release("stable"))?.Version == combined.Version, "Draft and prerelease versions are excluded");
            Check(Parse(Release("older", "1.9.0"), Release("newer", "1.10.0"), Release("oldest", "1.4.2"))?.Version == new Version(1, 10, 0, 0), "Version ordering is numeric and independent of release order");
            Check(Parse(Release("preview", "1.4.2-beta"), Release("bad", "not-a-version"), Release("uploading", state: "new")) == null, "Unsupported and incomplete desktop assets are ignored");
            Check(Parse(Release("evil", download: "https://example.com/malware.zip"), Release("evil2", page: "https://github.com/other/repo/releases/tag/evil2")) == null, "Only the expected repository release and asset links are accepted");
            Check(Parse(Release("http", page: Repo.Replace("https:", "http:") + "/releases/tag/http")) == null, "Non-HTTPS release links are rejected");
            Check(combined.Notes.Contains("中文更新说明") && combined.Download.AbsoluteUri.EndsWith("win-x64.zip"), "Notes and exact Windows download link are retained");
            var same = new DesktopUpdates(new Version(1, 4, 2), _ => Task.FromResult(combined));
            await same.CheckAsync();
            Check(same.State == UpdateCheckState.Current && !same.HasUpdate && same.CurrentVersionText == "1.4.2", "Three- and four-part equal versions do not prompt an update");
            var ahead = new DesktopUpdates(new Version(1, 5, 0), _ => Task.FromResult(combined));
            await ahead.CheckAsync();
            Check(ahead.State == UpdateCheckState.Current && !ahead.HasUpdate, "Development versions are never offered a downgrade");
            var gate = new TaskCompletionSource<DesktopRelease>(); int calls = 0;
            var shared = new DesktopUpdates(new Version(1, 4, 1), _ => { calls++; return gate.Task; });
            var first = shared.CheckAsync(); var second = shared.CheckAsync();
            Check(calls == 1 && ReferenceEquals(first, second) && shared.State == UpdateCheckState.Checking, "Concurrent manual and startup requests share one task");
            gate.SetResult(combined); await first;
            Check(shared.HasUpdate && shared.State == UpdateCheckState.Available, "Successful check makes a new version available");
            var error = await RunResponse(new(HttpStatusCode.Forbidden));
            Check(error.State == UpdateCheckState.Failed && !error.HasUpdate, "Rate limiting reports failure, not up-to-date");
            Check((await RunResponse(Ok("invalid JSON"))).State == UpdateCheckState.Failed, "Invalid JSON cannot crash or claim up-to-date");
            Check((await RunResponse(Ok("[]"))).State == UpdateCheckState.Failed, "An empty release list is not a successful up-to-date result");
            var disconnected = new DesktopUpdates(new Version(1, 4, 1), _ => throw new HttpRequestException("offline"));
            await disconnected.CheckAsync();
            Check(disconnected.State == UpdateCheckState.Failed, "Network failures are contained");
            var cancelled = new DesktopUpdates(new Version(1, 4, 1), _ => Task.FromCanceled<DesktopRelease>(new CancellationToken(true)));
            await cancelled.CheckAsync();
            Check(cancelled.State == UpdateCheckState.Failed, "Timeout and cancellation leave the checker usable");
            int retry = 0;
            var recover = new DesktopUpdates(new Version(1, 4, 1), _ => ++retry == 1 ? Task.FromException<DesktopRelease>(new IOException()) : Task.FromResult(combined));
            await recover.CheckAsync(); await recover.CheckAsync();
            Check(recover.HasUpdate && retry == 2, "Manual retry recovers after a failed check");
            int preserve = 0;
            var known = new DesktopUpdates(new Version(1, 4, 1), _ => ++preserve == 1 ? Task.FromResult(combined) : Task.FromException<DesktopRelease>(new IOException()));
            await known.CheckAsync(); await known.CheckAsync();
            Check(known.State == UpdateCheckState.Failed && known.HasUpdate, "A later network failure retains an already verified update link");
            int requests = 0;
            using (var http = new HttpClient(new Handler((request, _) => {
                requests++;
                Check(request.Method == HttpMethod.Get && request.Headers.UserAgent.Count > 0 && request.Headers.Authorization == null && request.Content == null,
                    "Public check sends no credentials or user data");
                return Task.FromResult(requests == 1 ? Ok(Json(Enumerable.Range(0, 100).Select(i => Release("plugin-" + i, null)).ToArray())) : Ok(Json(Release("combined"))));
            }))) {
                var latest = await new DesktopReleaseClient(http).FindLatestAsync(CancellationToken.None);
                Check(requests == 2 && latest.Version == combined.Version, "Pagination finds a desktop asset after a page of plugin releases");
            }
            int autoCalls = 0;
            var automatic = new DesktopUpdates(new Version(1, 4, 1), _ => { autoCalls++; return Task.FromResult(combined); });
            bool enabled = true;
            var startup = automatic.CheckAtStartupAsync(() => enabled); enabled = false;
            await startup;
            Check(autoCalls == 0, "Turning startup checks off during the delay prevents network access");
            await automatic.CheckAsync();
            Check(autoCalls == 1 && automatic.HasUpdate, "Manual checks work with automatic checking disabled");
            var manualFirst = new DesktopUpdates(new Version(1, 4, 1), _ => { autoCalls++; return Task.FromResult(combined); });
            var startup2 = manualFirst.CheckAtStartupAsync(() => true);
            await manualFirst.CheckAsync(); await startup2; await manualFirst.CheckAtStartupAsync(() => true);
            Check(autoCalls == 2, "Startup skips a completed manual check and only schedules once");
            var enabledStartup = new DesktopUpdates(new Version(1, 4, 1), _ => { autoCalls++; return Task.FromResult(combined); });
            var startOnce = enabledStartup.CheckAtStartupAsync(() => true);
            await enabledStartup.CheckAtStartupAsync(() => true); await startOnce;
            Check(autoCalls == 3 && enabledStartup.HasUpdate, "Enabled startup check runs once and discovers a newer client");
            Console.WriteLine($"All {checks} update checks passed."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
