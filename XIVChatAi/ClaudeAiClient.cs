using System.Text;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace XIVChatAi;

/// <summary>Claude through the official Anthropic SDK, with the user's own API key.</summary>
public sealed class ClaudeAiClient : IAiClient {
    private readonly AnthropicClient client;
    private readonly string model;

    /// <param name="baseUrl">Only for tests against a local fake server.</param>
    public ClaudeAiClient(string apiKey, string model, string? baseUrl = null) {
        this.model = model;
        this.client = baseUrl == null ? new AnthropicClient { ApiKey = apiKey } : new AnthropicClient { ApiKey = apiKey, BaseUrl = baseUrl };
    }

    internal static bool SupportsDefaultFallback(string model) =>
        model is "claude-opus-5-5" or "claude-opus-5" or "claude-fable-5-1" or "claude-sonnet-5-5";

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken token = default) {
        var output = new BetaOutputConfig { Effort = request.Effort == AiEffort.Low ? Effort.Low : Effort.Medium };
        var messages = new List<BetaMessageParam> { new() { Role = Role.User, Content = request.User } };
        // Thinking stays at the model default; effort sets how much it spends. Where the model supports it, a request a
        // safety classifier declines is retried server-side on a suitable model instead of returning nothing.
        var parameters = SupportsDefaultFallback(this.model)
            ? new MessageCreateParams {
                Model = this.model, MaxTokens = request.MaxTokens, System = request.System, OutputConfig = output, Messages = messages,
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01], Fallbacks = new Default(),
            }
            : new MessageCreateParams {
                Model = this.model, MaxTokens = request.MaxTokens, System = request.System, OutputConfig = output, Messages = messages,
            };
        var text = new StringBuilder();
        string? stopReason = null;
        try {
            // Streaming keeps long summaries clear of HTTP timeouts.
            await foreach (var streamEvent in this.client.Beta.Messages.CreateStreaming(parameters, token)) {
                if (streamEvent.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var chunk)) text.Append(chunk.Text);
                else if (streamEvent.TryPickDelta(out var messageDelta) && messageDelta.Delta.StopReason is { } reason) stopReason = reason.Raw();
            }
        } catch (AnthropicUnauthorizedException ex) {
            throw new AiException(AiFailure.Authentication, "The Claude API key was rejected.", ex);
        } catch (AnthropicRateLimitException ex) {
            throw new AiException(AiFailure.RateLimited, "Claude is rate limiting requests. Try again shortly.", ex);
        } catch (AnthropicApiException ex) {
            throw new AiException(AiFailure.Service, ex.Message, ex);
        } catch (HttpRequestException ex) {
            throw new AiException(AiFailure.Network, ex.Message, ex);
        }
        if (string.Equals(stopReason, "refusal", StringComparison.OrdinalIgnoreCase))
            throw new AiException(AiFailure.Refused, "Claude declined this request.");
        return text.ToString().Trim();
    }
}
