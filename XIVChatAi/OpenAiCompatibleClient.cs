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
        if (!Uri.TryCreate(baseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var root) || root.Scheme is not ("https" or "http"))
            throw new AiException(AiFailure.NotConfigured, "The service address must be an http or https URL.");
        // Plain HTTP would expose chat text and the key; allow it only for a model on this computer.
        if (root.Scheme == "http" && !root.IsLoopback)
            throw new AiException(AiFailure.NotConfigured, "Use https, or http only for a service on this computer.");
        this.endpoint = new Uri(root, "chat/completions");
        this.apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        this.model = model;
        this.http = http ?? SharedHttp;
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
