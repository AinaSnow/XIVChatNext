using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace XIVChat_Desktop;

public sealed record DesktopRelease(Version Version, Uri Page, Uri Download, string Notes);

// Release tags also identify plugin versions. Only a desktop ZIP identifies a desktop release.
public sealed class DesktopReleaseClient {
    private const string Repository = "https://github.com/AinaSnow/XIVChatNext";
    private const string Endpoint = "https://api.github.com/repos/AinaSnow/XIVChatNext/releases?per_page=100&page=";
    private static readonly Regex AssetName = new(@"^XIVChatNext-Desktop-v([0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?)-win-x64\.zip$", RegexOptions.CultureInvariant);
    private static readonly HttpClient SharedHttp = new();
    private readonly HttpClient http;
    public DesktopReleaseClient(HttpClient? http = null) => this.http = http ?? SharedHttp;

    public async Task<DesktopRelease> FindLatestAsync(CancellationToken token) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        DesktopRelease? newest = null;
        for (int page = 1; page <= 3; page++) {
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint + page);
            request.Headers.UserAgent.ParseAdd("XIVChatNext-Desktop-UpdateCheck/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            // Bound both the download and the parsed release notes; never download a ZIP during a check.
            await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var candidate = ReadPage(json, out int count);
            if (candidate != null && (newest == null || candidate.Version > newest.Version)) newest = candidate;
            if (count < 100) return newest ?? throw new InvalidDataException("No supported desktop release was found.");
        }
        throw new InvalidDataException("Release listing exceeded the check limit.");
    }

    internal static DesktopRelease? ReadPage(string json, out int count) {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid release list.");
        count = document.RootElement.GetArrayLength();
        DesktopRelease? newest = null;
        foreach (var release in document.RootElement.EnumerateArray()) {
            if (release.ValueKind != JsonValueKind.Object || !IsFalse(release, "draft") || !IsFalse(release, "prerelease")) continue;
            string? tag = Text(release, "tag_name");
            if (string.IsNullOrWhiteSpace(tag)) continue;
            var escapedTag = Uri.EscapeDataString(tag);
            var page = new Uri(Repository + "/releases/tag/" + escapedTag);
            if (!SameUrl(Text(release, "html_url"), page)) continue;
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            foreach (var asset in assets.EnumerateArray()) {
                if (asset.ValueKind != JsonValueKind.Object || Text(asset, "state") != "uploaded") continue;
                var name = Text(asset, "name") ?? "";
                var match = AssetName.Match(name);
                if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version)) continue;
                version = Normalize(version);
                var download = new Uri(Repository + "/releases/download/" + escapedTag + "/" + name);
                if (!SameUrl(Text(asset, "browser_download_url"), download)) continue;
                if (newest != null && newest.Version >= version) continue;
                var notes = Text(release, "body") ?? "";
                if (notes.Length > 24000) notes = notes[..24000] + "…";
                newest = new(version, page, download, notes);
            }
        }
        return newest;
    }

    internal static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    internal static string DisplayVersion(Version version) => version.Revision > 0 ? version.ToString(4) : version.ToString(3);
    private static bool IsFalse(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.False;
    private static string? Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool SameUrl(string? text, Uri expected) => Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Port == 443 && uri.AbsoluteUri == expected.AbsoluteUri;
}

public enum UpdateCheckState { Idle, Checking, Current, Available, Failed }

// Called on the UI context. A single in-flight task serves every settings window and startup.
public sealed class DesktopUpdates {
    public Version CurrentVersion { get; }
    public string CurrentVersionText => DesktopReleaseClient.DisplayVersion(CurrentVersion);
    public DesktopRelease? Release { get; private set; }
    public bool HasUpdate => Release != null && Release.Version > CurrentVersion;
    public UpdateCheckState State { get; private set; }
    public event Action? Changed;
    private readonly Func<CancellationToken, Task<DesktopRelease>> findLatest;
    private Task? pending;
    private bool startupRequested;

    public DesktopUpdates(Version currentVersion, Func<CancellationToken, Task<DesktopRelease>>? findLatest = null) {
        CurrentVersion = DesktopReleaseClient.Normalize(currentVersion);
        this.findLatest = findLatest ?? new DesktopReleaseClient().FindLatestAsync;
    }

    public async Task CheckAtStartupAsync(Func<bool> enabled) {
        if (startupRequested) return;
        startupRequested = true;
        await Task.Delay(TimeSpan.FromSeconds(3));
        // Re-check the preference after the window has opened, and don't repeat a manual check.
        if (enabled() && State == UpdateCheckState.Idle) await CheckAsync();
    }

    public Task CheckAsync() {
        if (pending is { IsCompleted: false }) return pending;
        return pending = CheckCoreAsync();
    }

    private async Task CheckCoreAsync() {
        State = UpdateCheckState.Checking;
        Changed?.Invoke();
        try {
            Release = await findLatest(CancellationToken.None);
            State = HasUpdate ? UpdateCheckState.Available : UpdateCheckState.Current;
        } catch (Exception) {
            // No raw network error, notification, chat message or fatal exception on startup.
            State = UpdateCheckState.Failed;
        }
        Changed?.Invoke();
    }
}
