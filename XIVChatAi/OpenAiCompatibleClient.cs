using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XIVChatAi;

/// <summary>
/// Any service exposing the OpenAI-style <c>/chat/completions</c> endpoint: hosted providers or a local model server
/// such as Ollama. Not an Anthropic API; Claude goes through <see cref="ClaudeAiClient"/>.
/// </summary>
public sealed class OpenAiCompatibleClient : IAiClient {
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly string? apiKey;
    private readonly string model;

    public OpenAiCompatibleClient(string baseUrl, string? apiKey, string model, HttpClient? http = null) {
        var root = ParseAddress(baseUrl);
        this.endpoint = new Uri(root, "chat/completions");
        this.apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        this.model = model;
        this.http = http ?? SharedHttp;
    }

    /// <summary>
    /// Validates a service address. Plain HTTP would expose chat text and the key on the way, so it is accepted only for
    /// this computer and private networks (for example a model server or proxy on the LAN); public hosts need https.
    /// </summary>
    public static Uri ParseAddress(string baseUrl) {
        if (!Uri.TryCreate(baseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var root) || root.Scheme is not ("https" or "http"))
            throw new AiException(AiFailure.InvalidAddress, "The service address must start with http:// or https://.");
        if (root.Scheme == "http" && !IsPrivateHost(root))
            throw new AiException(AiFailure.InsecureAddress, "Use https for services on the internet; http is only accepted on this computer or a private network.");
        return root;
    }

    public static bool IsPrivateHost(Uri uri) {
        if (uri.IsLoopback) return true;
        if (IPAddress.TryParse(uri.DnsSafeHost, out var address)) {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) {
                var b = address.GetAddressBytes();
                return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
                    || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127);
            }
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal;
        }
        var host = uri.DnsSafeHost.TrimEnd('.');
        // Single-label names and local suffixes are resolved on the local network.
        return !host.Contains('.') || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken token = default) {
        var body = new JsonObject {
            ["model"] = this.model,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = false,
            ["messages"] = new JsonArray {
                new JsonObject { ["role"] = "system", ["content"] = request.System },
                new JsonObject { ["role"] = "user", ["content"] = request.User },
            },
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, this.endpoint) {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (this.apiKey != null) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.apiKey);
        HttpResponseMessage response;
        try {
            response = await this.http.SendAsync(message, token);
        } catch (HttpRequestException ex) {
            throw new AiException(AiFailure.Network, ex.Message, ex);
        }
        using (response) {
            var text = await response.Content.ReadAsStringAsync(token);
            switch (response.StatusCode) {
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    throw new AiException(AiFailure.Authentication, "The service rejected the API key.");
                case HttpStatusCode.TooManyRequests:
                    throw new AiException(AiFailure.RateLimited, "The service is rate limiting requests. Try again shortly.");
                case HttpStatusCode.RequestEntityTooLarge:
                    throw new AiException(AiFailure.TooLarge, "The request is too large for this service.");
            }
            if (!response.IsSuccessStatusCode) throw new AiException(AiFailure.Service, $"The service returned {(int)response.StatusCode}.");
            try {
                var content = JsonNode.Parse(text)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
                return content?.Trim() ?? throw new AiException(AiFailure.Service, "The service returned no text.");
            } catch (JsonException ex) {
                throw new AiException(AiFailure.Service, "The service returned an unreadable response.", ex);
            }
        }
    }
}
