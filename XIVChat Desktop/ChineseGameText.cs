using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XIVChat_Desktop {
    public sealed class ChineseTextRow {
        public uint Id { get; set; }
        public string Table { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string ClassJobs { get; set; } = "";
        public Dictionary<uint, string> Parameters { get; set; } = new();
        public string Version { get; set; } = "";
        public string Schema { get; set; } = "";
        public long CapturedAt { get; set; }
    }
    // Translations are separate from game DTOs: remote data can never change gameplay numbers.
    public sealed class ChineseGameText {
        public const string BaseUrl = "https://xivapi-v2.xivcdn.com";
        private readonly HttpClient http;
        private readonly string directory;
        private readonly SemaphoreSlim requests = new(2);
        private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(7) };
        private const int MaxBytes = 256_000;
        private static readonly HashSet<string> Tables = new() { "Item", "EventItem", "Map", "PlaceName", "BaseParam", "CraftType", "GatheringType", "ENpcResident", "GilShop" };
        public ChineseGameText(string directory, HttpClient? http = null) { this.directory = directory; this.http = http ?? SharedHttp; }
        private string PathFor(string key) => Path.Combine(this.directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
        private async Task<ChineseTextRow?> Read(string key, CancellationToken token) {
            try {
                var file = new FileInfo(this.PathFor(key)); if (!file.Exists || file.Length > MaxBytes) return null;
                var row = JsonSerializer.Deserialize<ChineseTextRow>(await File.ReadAllTextAsync(file.FullName, token));
                return row != null && row.Name?.Length <= 512 && row.Description?.Length <= 8192 && row.Parameters?.Count <= 32 ? row : null;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
        }
        private async Task Save(string key, ChineseTextRow row, CancellationToken token) {
            string? temporary = null;
            try {
                Directory.CreateDirectory(this.directory); var path = this.PathFor(key); temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(row), token); File.Move(temporary, path, true); temporary = null;
                foreach (var old in new DirectoryInfo(this.directory).EnumerateFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(1024)) old.Delete();
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally { if (temporary != null) { try { File.Delete(temporary); } catch (IOException) { } } }
        }
        private static bool Fresh(ChineseTextRow? row) => row != null && row.CapturedAt > 0 &&
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - row.CapturedAt is >= 0 and < 604800000;
        private async Task<JsonDocument> Fetch(string path, CancellationToken token) {
            await this.requests.WaitAsync(token);
            try {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using var response = await this.http.GetAsync(BaseUrl + path, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException("Chinese text response exceeds budget.");
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token); using var buffer = new MemoryStream();
                var block = new byte[8192]; int read;
                while ((read = await stream.ReadAsync(block, timeout.Token)) > 0) {
                    if (buffer.Length + read > MaxBytes) throw new InvalidDataException("Chinese text response exceeds budget.");
                    buffer.Write(block, 0, read);
                }
                return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
            } finally { this.requests.Release(); }
        }
        private static string Text(JsonElement element, string name, int limit = 512) => element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? XIVChatCommon.CardProtocol.Text(value.GetString(), limit) : "";
        private static string ReferenceName(JsonElement fields, string key, string name = "Name") => fields.TryGetProperty(key, out var row) &&
            row.ValueKind == JsonValueKind.Object && row.TryGetProperty("fields", out var nested) ? Text(nested, name) : "";
        private static ChineseTextRow Parse(string table, JsonElement row, JsonElement root) {
            var fields = row.GetProperty("fields");
            var result = new ChineseTextRow { Table = table, Id = row.GetProperty("row_id").GetUInt32(),
                Name = table == "Map" ? ReferenceName(fields, "PlaceName") : Text(fields, "Name"), Description = Text(fields, "Description", 8192),
                Category = ReferenceName(fields, "ItemUICategory"), ClassJobs = ReferenceName(fields, "ClassJobCategory"),
                Version = Text(root, "version", 128), Schema = Text(root, "schema", 192), CapturedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            if (result.Name.Length == 0) result.Name = Text(fields, "Singular");
            foreach (var key in new[] { "BaseParam", "BaseParamSpecial" }) {
                if (!fields.TryGetProperty(key, out var parameters) || parameters.ValueKind != JsonValueKind.Array) continue;
                foreach (var parameter in parameters.EnumerateArray().Take(16)) {
                    if (parameter.ValueKind != JsonValueKind.Object || !parameter.TryGetProperty("row_id", out var id) || !id.TryGetUInt32(out var value) || value == 0 ||
                        !parameter.TryGetProperty("fields", out var name)) continue;
                    var label = Text(name, "Name", 256); if (label.Length > 0) result.Parameters[value] = label;
                }
            }
            return result;
        }
        public Task<ChineseTextRow?> ItemAsync(uint id, bool eventItem, bool refresh, CancellationToken token) => this.RowAsync(id, eventItem ? "EventItem" : "Item",
            eventItem ? "Name,Singular" : "Name,Description,ItemUICategory.Name,ClassJobCategory.Name,BaseParam[].Name,BaseParamSpecial[].Name", refresh, token);
        public Task<ChineseTextRow?> MapAsync(uint id, bool refresh, CancellationToken token) => this.RowAsync(id, "Map", "PlaceName.Name", refresh, token);
        private async Task<ChineseTextRow?> RowAsync(uint id, string table, string fields, bool refresh, CancellationToken token) {
            var key = "full/chs/" + table + "/" + id;
            var cached = await this.Read(key, token);
            if (cached?.Id != id || cached?.Table != table) cached = null;
            if (!refresh && Fresh(cached)) return cached;
            try {
                using var json = await this.Fetch($"/api/sheet/{table}/{id}?language=chs&fields={Uri.EscapeDataString(fields)}", token);
                var result = Parse(table, json.RootElement, json.RootElement);
                if (result.Id != id || result.Name.Length == 0) return cached;
                await this.Save(key, result, token); return result;
            } catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException or InvalidOperationException or KeyNotFoundException or FormatException) {
                token.ThrowIfCancellationRequested(); return cached;
            }
        }
        public async Task<Dictionary<uint, string>> NamesAsync(string table, IEnumerable<uint> ids, CancellationToken token, bool refresh = false) {
            if (!Tables.Contains(table)) throw new ArgumentException("Unsupported game text table.");
            var result = new Dictionary<uint, string>(); var missing = new List<uint>();
            foreach (var id in ids.Distinct().Take(64)) {
                var cached = await this.Read("name/chs/" + table + "/" + id, token);
                if (cached?.Id != id || cached?.Table != table) cached = null;
                if (cached?.Id == id && cached.Table == table && cached.Name.Length > 0) result[id] = cached.Name;
                if (refresh || !Fresh(cached)) missing.Add(id);
            }
            foreach (var batch in missing.Chunk(32)) {
                try {
                    var fields = table == "Map" ? "PlaceName.Name" : table is "EventItem" or "ENpcResident" ? "Singular" : "Name";
                    using var json = await this.Fetch($"/api/sheet/{table}?language=chs&rows={string.Join(",", batch)}&fields={fields}", token);
                    foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray().Take(batch.Length)) {
                        var parsed = Parse(table, row, json.RootElement);
                        if (!batch.Contains(parsed.Id) || parsed.Name.Length == 0) continue;
                        result[parsed.Id] = parsed.Name; await this.Save("name/chs/" + table + "/" + parsed.Id, parsed, token);
                    }
                } catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException or InvalidOperationException or KeyNotFoundException or FormatException) {
                    token.ThrowIfCancellationRequested();
                }
            }
            return result;
        }
    }
}
