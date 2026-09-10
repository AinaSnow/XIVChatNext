using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XIVChatCommon.Message;
using XIVChatStorage;

namespace XIVChat_Desktop {
    public sealed record LodestoneProfile(string Id, string Name, string World, Uri Avatar);

    public static class LodestoneParser {
        private static readonly TimeSpan RegexLimit = TimeSpan.FromMilliseconds(300);
        private static Match Match(string text, string pattern) => Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexLimit);
        private static string Plain(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", "", RegexOptions.Singleline, RegexLimit)).Trim();
        private static string TextClass(string html, string css) => Plain(Match(html, "<[^>]+class=[\"'][^\"']*\\b" + Regex.Escape(css) + "\\b[^\"']*[\"'][^>]*>(.*?)</(?:p|div|span|li)>").Groups[1].Value);
        public static bool OfficialPage(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
            new[] { "na", "eu", "jp", "fr", "de" }.Any(region => uri.Host.Equals(region + ".finalfantasyxiv.com", StringComparison.OrdinalIgnoreCase));
        public static bool OfficialImage(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
            new[] { "img2.finalfantasyxiv.com", "lds-img.finalfantasyxiv.com" }.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
        public static string? ExtractId(string input) {
            input = input.Trim();
            if (Regex.IsMatch(input, "^[1-9][0-9]{0,11}$", RegexOptions.None, RegexLimit)) return input;
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || !OfficialPage(uri)) return null;
            var match = Match(uri.AbsolutePath, "^/lodestone/character/([1-9][0-9]{0,11})/?$");
            return match.Success ? match.Groups[1].Value : null;
        }
        public static Uri ProfileUrl(string id) => new("https://na.finalfantasyxiv.com/lodestone/character/" + id + "/");
        public static string? FindExact(string html, string name, string world) {
            var matches = new HashSet<string>();
            foreach (Match entry in Regex.Matches(html, "<a\\b(?=[^>]*class=[\"'][^\"']*\\bentry__link\\b[^\"']*[\"'])[^>]*href=[\"'](/lodestone/character/[0-9]+/)[\"'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexLimit)) {
                var body = entry.Groups[2].Value;
                var actualWorld = TextClass(body, "entry__world").Split('[')[0].Trim();
                if (string.Equals(TextClass(body, "entry__name"), name, StringComparison.OrdinalIgnoreCase) && string.Equals(actualWorld, world, StringComparison.OrdinalIgnoreCase))
                    matches.Add(ExtractId(new Uri(ProfileUrl("1"), entry.Groups[1].Value).AbsoluteUri)!);
            }
            if (matches.Count != 1) return null;
            var pages = Regex.Matches(TextClass(html, "btn__pager__current"), "[0-9]+", RegexOptions.None, RegexLimit).Select(m => m.Value).ToArray();
            // A truncated or paginated result cannot establish uniqueness.
            return pages.Length == 2 && pages[0] == "1" && pages[1] == "1" ? matches.Single() : null;
        }
        public static LodestoneProfile ParseProfile(string html, string id) {
            var name = TextClass(html, "frame__chara__name");
            var world = TextClass(html, "frame__chara__world").Split('[')[0].Trim();
            var face = Match(html, "<div[^>]*class=[\"'][^\"']*\\bframe__chara__face\\b[^\"']*[\"'][^>]*>(.*?)</div>").Groups[1].Value;
            var url = WebUtility.HtmlDecode(Match(face, "<img[^>]+src=[\"']([^\"']+)[\"']").Groups[1].Value);
            if (name.Length == 0 || world.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var avatar) || !OfficialImage(avatar))
                throw new FormatException("Lodestone profile layout is unavailable.");
            return new LodestoneProfile(id, name, world, avatar);
        }
    }

    /// <summary>Visible identities request cached avatars. Online work is coalesced, serialized and cancellable.</summary>
    public sealed class LodestoneAvatars : IDisposable {
        private readonly Func<HistoryStore?> store;
        private readonly string directory;
        private readonly HttpClient http;
        private readonly SemaphoreSlim network = new(1);
        private readonly ConcurrentDictionary<string, AvatarMapping> cache = new();
        private readonly ConcurrentDictionary<string, Lazy<Task<AvatarMapping?>>> requests = new();
        private readonly ConcurrentDictionary<string, int> revisions = new();
        private readonly SemaphoreSlim mutations = new(1);
        private CancellationTokenSource online = new();
        private bool enabled;
        private bool disposed;
        private DateTime nextRequest;
        public event Action<string>? Changed;
        public LodestoneAvatars(Func<HistoryStore?> store, string directory, bool enabled, HttpMessageHandler? handler = null) {
            this.store = store; this.directory = directory; this.enabled = enabled;
            this.http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("XIVChatNext/1.0 (Lodestone avatar lookup)");
        }
        public void SetEnabled(bool value) {
            if (enabled == value) return;
            enabled = value; online.Cancel(); online = new CancellationTokenSource();
        }
        public async Task<AvatarMapping?> GetAsync(CharacterIdentity identity, bool refresh = false) {
            var key = ConversationIdentity.PeerKey(identity);
            if (key == null || disposed) return null;
            AvatarMapping? saved;
            Lazy<Task<AvatarMapping?>> job;
            await mutations.WaitAsync();
            try {
                if (!cache.TryGetValue(key, out saved)) {
                    try { saved = store() is { } db ? await db.GetAvatarAsync(key) : null; if (saved != null) cache[key] = saved; }
                    catch { return null; }
                }
                if (!enabled || disposed || saved?.Disabled == true) return saved;
                if (!refresh && saved != null && ((saved.RetryAt > DateTime.UtcNow) || (saved.CheckedAt > DateTime.UtcNow.AddHours(-24) && ValidLocalPath(saved.ImagePath)))) return saved;
                if (requests.Count >= 32 && !requests.ContainsKey(key)) return saved;
                var copy = ConversationIdentity.Copy(identity);
                var revision = revisions.GetOrAdd(key, 0);
                var token = online.Token;
                job = requests.GetOrAdd(key, _ => new Lazy<Task<AvatarMapping?>>(() => RefreshAsync(copy, saved, revision, token)));
            } finally { mutations.Release(); }
            if (saved?.ImagePath != null && !refresh) { _ = CompleteAsync(key, job); return saved; }
            return await CompleteAsync(key, job);
        }
        private async Task<AvatarMapping?> CompleteAsync(string key, Lazy<Task<AvatarMapping?>> job) {
            try { return await job.Value; }
            finally { requests.TryRemove(new KeyValuePair<string, Lazy<Task<AvatarMapping?>>>(key, job)); }
        }
        private async Task<AvatarMapping?> RefreshAsync(CharacterIdentity identity, AvatarMapping? previous, int revision, CancellationToken token) {
            var key = ConversationIdentity.PeerKey(identity)!;
            try {
                string? id = previous?.LodestoneId;
                if (id == null) {
                    var url = new Uri("https://na.finalfantasyxiv.com/lodestone/character/?q=" + Uri.EscapeDataString(identity.Name) + "&worldname=" + Uri.EscapeDataString(identity.HomeWorld));
                    id = LodestoneParser.FindExact(await HtmlAsync(url, token), identity.Name, identity.HomeWorld);
                }
                if (id == null) return await CommitAsync(previous ?? new AvatarMapping(key, null, false, null, null, null), revision, token, false);
                var profile = LodestoneParser.ParseProfile(await HtmlAsync(LodestoneParser.ProfileUrl(id), token), id);
                if (previous?.Manual != true && (!string.Equals(profile.Name, identity.Name, StringComparison.OrdinalIgnoreCase) || !string.Equals(profile.World, identity.HomeWorld, StringComparison.OrdinalIgnoreCase)))
                    throw new FormatException("Lodestone identity no longer matches.");
                var bytes = await DownloadAsync(profile.Avatar, true, token);
                if (!(bytes.Length >= 8 && (bytes[0] == 0xff && bytes[1] == 0xd8 || bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))))
                    throw new FormatException("Unsupported avatar image.");
                token.ThrowIfCancellationRequested();
                var file = Convert.ToHexString(SHA256.HashData(bytes)) + (bytes[0] == 0xff ? ".jpg" : ".png");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, file);
                await File.WriteAllBytesAsync(path, bytes, token);
                return await CommitAsync(new AvatarMapping(key, id, previous?.Manual == true, path, DateTime.UtcNow, null), revision, token, true);
            } catch (OperationCanceledException) { return cache.GetValueOrDefault(key) ?? previous; }
            catch { return await CommitAsync(previous ?? new AvatarMapping(key, null, false, null, null, null), revision, token, false); }
        }
        private async Task<AvatarMapping?> CommitAsync(AvatarMapping value, int revision, CancellationToken token, bool success) {
            if (!success) value = value with { RetryAt = DateTime.UtcNow.AddHours(1) };
            await mutations.WaitAsync();
            try {
                if (token.IsCancellationRequested || revisions.GetValueOrDefault(value.CharacterKey) != revision || disposed) return cache.GetValueOrDefault(value.CharacterKey);
                cache[value.CharacterKey] = value;
                try { if (store() is { } db) await db.SaveAvatarAsync(value); } catch { }
                Changed?.Invoke(value.CharacterKey); return value;
            } finally { mutations.Release(); }
        }
        public async Task<AvatarMapping?> BindAsync(CharacterIdentity identity, string? input, bool autoFind = false) {
            var key = ConversationIdentity.PeerKey(identity) ?? throw new ArgumentException("Incomplete identity.");
            var id = input == null ? null : LodestoneParser.ExtractId(input) ?? throw new ArgumentException("Invalid Lodestone ID or profile URL.");
            await mutations.WaitAsync();
            try {
                revisions.AddOrUpdate(key, 1, (_, value) => value + 1);
                requests.TryRemove(key, out _);
                var mapping = new AvatarMapping(key, id, id != null, null, null, null, id == null && !autoFind);
                cache[key] = mapping;
                if (store() is { } db) await db.SaveAvatarAsync(mapping);
                Changed?.Invoke(key);
            } finally { mutations.Release(); }
            return await GetAsync(identity, true);
        }
        public bool ValidLocalPath(string? path) => path != null && Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        private async Task<string> HtmlAsync(Uri uri, CancellationToken token) => Encoding.UTF8.GetString(await DownloadAsync(uri, false, token));
        private async Task<byte[]> DownloadAsync(Uri uri, bool image, CancellationToken token) {
            await network.WaitAsync(token);
            try {
                for (int redirects = 0; redirects < 4; redirects++) {
                    if (!(image ? LodestoneParser.OfficialImage(uri) : LodestoneParser.OfficialPage(uri))) throw new InvalidDataException("Unexpected Lodestone host.");
                    var delay = nextRequest - DateTime.UtcNow;
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
                    nextRequest = DateTime.UtcNow.AddSeconds(1);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(12));
                    var requestToken = deadline.Token;
                    using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, requestToken);
                    if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } redirect) { uri = new Uri(uri, redirect); continue; }
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new InvalidDataException("Lodestone response too large.");
                    using var body = await response.Content.ReadAsStreamAsync(requestToken);
                    using var buffer = new MemoryStream();
                    var block = new byte[8192]; int read;
                    while ((read = await body.ReadAsync(block, requestToken)) > 0) {
                        if (buffer.Length + read > 2 * 1024 * 1024) throw new InvalidDataException("Lodestone response too large.");
                        buffer.Write(block, 0, read);
                    }
                    return buffer.ToArray();
                }
                throw new InvalidDataException("Too many Lodestone redirects.");
            } finally { network.Release(); }
        }
        public void Dispose() { disposed = true; online.Cancel(); http.Dispose(); }
    }
}
