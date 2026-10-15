namespace XIVChatAi;

public enum AiProviderKind { Claude, OpenAiCompatible }

public enum AiEffort { Low, Medium }

/// <param name="System">Instructions for the model; chat text never goes here.</param>
/// <param name="User">The task and the chat text, wrapped so the model treats it as data.</param>
public sealed record AiRequest(string System, string User, AiEffort Effort, int MaxTokens);

public enum AiFailure { NotConfigured, Authentication, RateLimited, Refused, TooLarge, Network, Service, InvalidAddress, InsecureAddress }

public sealed class AiException(AiFailure failure, string detail, Exception? inner = null, string? setting = null) : Exception(detail, inner) {
    public AiFailure Failure { get; } = failure;
    /// <summary>For <see cref="AiFailure.NotConfigured"/>: the missing setting, e.g. "BaseUrl", so the app can name it.</summary>
    public string? Setting { get; } = setting;

    public static AiException Missing(string setting) => new(AiFailure.NotConfigured, $"Missing setting: {setting}.", setting: setting);
}

/// <summary>One completion from the configured provider. Implementations must be safe to call concurrently.</summary>
public interface IAiClient {
    Task<string> CompleteAsync(AiRequest request, CancellationToken token = default);
}

public sealed record AiSettings(AiProviderKind Provider, string? ApiKey, string Model, string? BaseUrl = null) {
    public const string DefaultClaudeModel = "claude-opus-5-5";

    public IAiClient CreateClient() => this.Provider switch {
        AiProviderKind.Claude => string.IsNullOrWhiteSpace(this.ApiKey) ? throw AiException.Missing("ClaudeKey")
            : new ClaudeAiClient(this.ApiKey!, string.IsNullOrWhiteSpace(this.Model) ? DefaultClaudeModel : this.Model.Trim(), this.BaseUrl),
        _ => string.IsNullOrWhiteSpace(this.BaseUrl) ? throw AiException.Missing("BaseUrl")
            : string.IsNullOrWhiteSpace(this.Model) ? throw AiException.Missing("Model")
            : new OpenAiCompatibleClient(this.BaseUrl!, this.ApiKey, this.Model.Trim()),
    };
}
