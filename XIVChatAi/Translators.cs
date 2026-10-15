using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XIVChatAi;

public enum TranslationProvider { GoogleFree, Microsoft, DeepL, Baidu, Tencent, Ai }

/// <summary>Target languages offered for translation, as neutral codes each provider maps to its own.</summary>
public static class TranslationLanguages {
    public static readonly string[] Codes = ["zh-Hans", "zh-Hant", "en", "ja", "ko", "de", "fr"];

    /// <summary>English name, used in AI prompts.</summary>
    public static string Name(string code) => code switch {
        "zh-Hans" => "Simplified Chinese", "zh-Hant" => "Traditional Chinese", "ja" => "Japanese", "ko" => "Korean",
        "de" => "German", "fr" => "French", _ => "English",
    };

    internal static string For(TranslationProvider provider, string code) => (provider, code) switch {
        (TranslationProvider.GoogleFree, "zh-Hans") => "zh-CN",
        (TranslationProvider.GoogleFree, "zh-Hant") => "zh-TW",
        (TranslationProvider.DeepL, "zh-Hans") => "ZH-HANS",
        (TranslationProvider.DeepL, "zh-Hant") => "ZH-HANT",
        (TranslationProvider.DeepL, "en") => "EN-US",
        (TranslationProvider.DeepL, _) => code.ToUpperInvariant(),
        (TranslationProvider.Baidu, "zh-Hans") => "zh",
        (TranslationProvider.Baidu, "zh-Hant") => "cht",
        (TranslationProvider.Baidu, "ja") => "jp",
        (TranslationProvider.Baidu, "ko") => "kor",
        (TranslationProvider.Baidu, "fr") => "fra",
        (TranslationProvider.Tencent, "zh-Hans") => "zh",
        (TranslationProvider.Tencent, "zh-Hant") => "zh-TW",
        _ => code,
    };
}

/// <summary>Translates one chat message. Errors are reported as <see cref="AiException"/>.</summary>
public interface ITranslator {
    Task<string> TranslateAsync(string text, string targetCode, CancellationToken token = default);
}

/// <summary>Translation through the configured AI service, which also keeps game terms and names intact.</summary>
public sealed class AiTranslator(IAiClient client) : ITranslator {
    public Task<string> TranslateAsync(string text, string targetCode, CancellationToken token = default) =>
        client.CompleteAsync(AiPrompts.Translation(text, TranslationLanguages.Name(targetCode)), token);
}

/// <summary>Shared HTTP handling for the machine-translation services.</summary>
public abstract class HttpTranslator : ITranslator {
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
    protected readonly HttpClient Http;
    protected HttpTranslator(HttpClient? http) => this.Http = http ?? SharedHttp;
    protected abstract TranslationProvider Provider { get; }

    public async Task<string> TranslateAsync(string text, string targetCode, CancellationToken token = default) {
        if (text.Length > AiPrompts.MaxTranslationCharacters) throw new AiException(AiFailure.TooLarge, "This message is too long to translate.");
        using var request = this.Build(text, TranslationLanguages.For(this.Provider, targetCode));
        HttpResponseMessage response;
        try {
            response = await this.Http.SendAsync(request, token);
        } catch (HttpRequestException ex) {
            throw new AiException(AiFailure.Network, ex.Message, ex);
        } catch (TaskCanceledException ex) when (!token.IsCancellationRequested) {
            throw new AiException(AiFailure.Network, "The translation service did not answer in time.", ex);
        }
        using (response) {
            var body = await response.Content.ReadAsStringAsync(token);
            switch (response.StatusCode) {
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    throw new AiException(AiFailure.Authentication, "The translation service rejected the key.");
                case HttpStatusCode.TooManyRequests or (HttpStatusCode)456:
                    throw new AiException(AiFailure.RateLimited, "The translation service limit was reached.");
            }
            if (!response.IsSuccessStatusCode) throw new AiException(AiFailure.Service, $"The translation service returned {(int)response.StatusCode}.");
            try {
                var result = this.Parse(JsonNode.Parse(body));
                return string.IsNullOrWhiteSpace(result) ? throw new AiException(AiFailure.Service, "The translation service returned no text.") : result.Trim();
            } catch (JsonException ex) {
                throw new AiException(AiFailure.Service, "The translation service returned an unreadable response.", ex);
            }
        }
    }

    protected abstract HttpRequestMessage Build(string text, string target);
    protected abstract string? Parse(JsonNode? response);

    protected static HttpRequestMessage Json(Uri uri, JsonNode body) =>
        new(HttpMethod.Post, uri) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}

/// <summary>
/// Google Translate's keyless web endpoint. It is not a published API: Google may change or block it, its terms do not
/// cover automated use, and it is unreachable from mainland China without a proxy. Offered because it needs no setup.
/// </summary>
public sealed class GoogleFreeTranslator(HttpClient? http = null, string baseUrl = "https://translate.googleapis.com/") : HttpTranslator(http) {
    protected override TranslationProvider Provider => TranslationProvider.GoogleFree;
    protected override HttpRequestMessage Build(string text, string target) =>
        new(HttpMethod.Post, new Uri(new Uri(baseUrl), "translate_a/single?client=gtx&sl=auto&dt=t&tl=" + Uri.EscapeDataString(target))) {
            Content = new FormUrlEncodedContent([new("q", text)]),
        };
    // [[["translated segment","original segment",...],...],...]
    protected override string? Parse(JsonNode? response) =>
        response?[0] is JsonArray segments ? string.Concat(segments.Select(s => s?[0]?.GetValue<string>() ?? "")) : null;
}

/// <summary>Microsoft Translator (Azure AI Translator) with the user's key; the region is required for regional resources.</summary>
public sealed class MicrosoftTranslator(string key, string? region, HttpClient? http = null, string baseUrl = "https://api.cognitive.microsofttranslator.com/") : HttpTranslator(http) {
    protected override TranslationProvider Provider => TranslationProvider.Microsoft;
    protected override HttpRequestMessage Build(string text, string target) {
        var request = Json(new Uri(new Uri(baseUrl), "translate?api-version=3.0&to=" + Uri.EscapeDataString(target)), new JsonArray { new JsonObject { ["Text"] = text } });
        request.Headers.Add("Ocp-Apim-Subscription-Key", key);
        if (!string.IsNullOrWhiteSpace(region)) request.Headers.Add("Ocp-Apim-Subscription-Region", region.Trim());
        return request;
    }
    protected override string? Parse(JsonNode? response) => response?[0]?["translations"]?[0]?["text"]?.GetValue<string>();
}

/// <summary>DeepL with the user's key. Free-plan keys end in ":fx" and use the free endpoint.</summary>
public sealed class DeepLTranslator(string key, HttpClient? http = null, string? baseUrl = null) : HttpTranslator(http) {
    protected override TranslationProvider Provider => TranslationProvider.DeepL;
    protected override HttpRequestMessage Build(string text, string target) {
        var root = baseUrl ?? (key.EndsWith(":fx", StringComparison.Ordinal) ? "https://api-free.deepl.com/" : "https://api.deepl.com/");
        var request = Json(new Uri(new Uri(root), "v2/translate"), new JsonObject { ["text"] = new JsonArray { text }, ["target_lang"] = target });
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", key);
        return request;
    }
    protected override string? Parse(JsonNode? response) => response?["translations"]?[0]?["text"]?.GetValue<string>();
}

/// <summary>Baidu Translate (fanyi-api.baidu.com) with the user's APP ID and secret key.</summary>
public sealed class BaiduTranslator(string appId, string secret, HttpClient? http = null, string baseUrl = "https://fanyi-api.baidu.com/") : HttpTranslator(http) {
    protected override TranslationProvider Provider => TranslationProvider.Baidu;
    protected override HttpRequestMessage Build(string text, string target) {
        var salt = RandomNumberGenerator.GetInt32(100_000, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        return new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "api/trans/vip/translate")) {
            Content = new FormUrlEncodedContent([
                new("q", text), new("from", "auto"), new("to", target), new("appid", appId), new("salt", salt),
                new("sign", Sign(appId, text, salt, secret)),
            ]),
        };
    }
    /// <summary>MD5 of appid + query + salt + secret, lower-case hex, as Baidu specifies.</summary>
    public static string Sign(string appId, string text, string salt, string secret) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(appId + text + salt + secret)));
    protected override string? Parse(JsonNode? response) {
        if (response?["error_code"] is { } code) {
            var value = code.ToString();
            throw value switch {
                "52003" or "54001" => new AiException(AiFailure.Authentication, "Baidu rejected the APP ID or key."),
                "54003" or "54004" or "54005" => new AiException(AiFailure.RateLimited, "The Baidu translation limit was reached."),
                _ => new AiException(AiFailure.Service, $"Baidu returned error {value}."),
            };
        }
        return response?["trans_result"] is JsonArray lines ? string.Join("\n", lines.Select(l => l?["dst"]?.GetValue<string>() ?? "")) : null;
    }
}

/// <summary>Tencent Cloud Machine Translation (TextTranslate) with the user's SecretId and SecretKey, signed with TC3-HMAC-SHA256.</summary>
public sealed class TencentTranslator(string secretId, string secretKey, string? region, HttpClient? http = null,
    string host = "tmt.tencentcloudapi.com", Func<DateTimeOffset>? clock = null) : HttpTranslator(http) {
    private const string Service = "tmt", Action = "TextTranslate", Version = "2018-03-21";
    protected override TranslationProvider Provider => TranslationProvider.Tencent;

    protected override HttpRequestMessage Build(string text, string target) {
        var payload = new JsonObject { ["SourceText"] = text, ["Source"] = "auto", ["Target"] = target, ["ProjectId"] = 0 }.ToJsonString();
        var now = (clock ?? (() => DateTimeOffset.UtcNow))();
        var scheme = host.StartsWith("127.0.0.1", StringComparison.Ordinal) || host.StartsWith("localhost", StringComparison.Ordinal) ? "http" : "https";
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{scheme}://{host}/")) {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
        request.Headers.TryAddWithoutValidation("Authorization", Authorization(secretId, secretKey, host, payload, now));
        request.Headers.Add("X-TC-Action", Action);
        request.Headers.Add("X-TC-Version", Version);
        request.Headers.Add("X-TC-Timestamp", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("X-TC-Region", string.IsNullOrWhiteSpace(region) ? "ap-guangzhou" : region.Trim());
        return request;
    }

    public static string Authorization(string secretId, string secretKey, string host, string payload, DateTimeOffset now) {
        static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
        static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
        var date = now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var canonical = "POST\n/\n\ncontent-type:application/json; charset=utf-8\nhost:" + host + "\nx-tc-action:" + Action.ToLowerInvariant() +
            "\n\ncontent-type;host;x-tc-action\n" + Hex(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var scope = $"{date}/{Service}/tc3_request";
        var toSign = $"TC3-HMAC-SHA256\n{now.ToUnixTimeSeconds()}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}";
        var signing = Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("TC3" + secretKey), date), Service), "tc3_request");
        return $"TC3-HMAC-SHA256 Credential={secretId}/{scope}, SignedHeaders=content-type;host;x-tc-action, Signature={Hex(Hmac(signing, toSign))}";
    }

    protected override string? Parse(JsonNode? response) {
        var body = response?["Response"];
        if (body?["Error"] is { } error) {
            var code = error["Code"]?.ToString() ?? "";
            throw code.StartsWith("AuthFailure", StringComparison.Ordinal) ? new AiException(AiFailure.Authentication, "Tencent Cloud rejected the SecretId or SecretKey.")
                : code.StartsWith("RequestLimitExceeded", StringComparison.Ordinal) || code.Contains("LimitExceeded") ? new AiException(AiFailure.RateLimited, "The Tencent translation limit was reached.")
                : new AiException(AiFailure.Service, $"Tencent Cloud returned {code}.");
        }
        return body?["TargetText"]?.GetValue<string>();
    }
}
