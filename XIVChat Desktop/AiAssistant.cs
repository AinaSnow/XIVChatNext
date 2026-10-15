using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XIVChat.Relay.Transport;
using XIVChatAi;
using XIVChatCommon.Message.Server;
using XIVChatStorage;

namespace XIVChat_Desktop {
    public enum TranslationStatus { Pending, Done, Failed }

    public sealed record TranslationState(TranslationStatus Status, string Text);

    /// <summary>
    /// Message translation and history summaries through the user's own AI service. Only text the user asks for is sent,
    /// with names masked the same way as exports when streamer mode is on.
    /// </summary>
    public sealed class AiAssistant {
        private readonly App app;
        // The same message is rendered from live and stored copies, so translations are keyed by its stable id.
        private const int MaxTranslations = 500;
        private readonly Dictionary<string, TranslationState> translations = new();
        private readonly Queue<string> translationOrder = new();
        private readonly ConditionalWeakTable<ServerMessage, string> unnamed = new();

        /// <summary>Raised on the UI thread when a message's translation starts, finishes or fails.</summary>
        public event Action<ServerMessage>? TranslationChanged;
        /// <summary>Tests replace the client to avoid calling a real service.</summary>
        internal Func<AiSettings, IAiClient> ClientFactory { get; set; } = settings => settings.CreateClient();

        public AiAssistant(App app) => this.app = app;

        private AiOptions Options => this.app.Config.Ai;
        public bool Enabled => this.Options.Enabled;

        public string TargetLanguage => !string.IsNullOrWhiteSpace(this.Options.TargetLanguage) ? this.Options.TargetLanguage.Trim()
            : (LocalizationHelper.LanguageCode.Length > 0 ? LocalizationHelper.LanguageCode : System.Globalization.CultureInfo.CurrentUICulture.Name)
                .StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "Simplified Chinese" : "English";

        public AiSettings Settings() => this.Options.Provider == AiProviderKind.Claude
            ? new AiSettings(AiProviderKind.Claude, Unprotect(this.Options.ProtectedClaudeKey), this.Options.ClaudeModel)
            : new AiSettings(AiProviderKind.OpenAiCompatible, Unprotect(this.Options.ProtectedCompatibleKey), this.Options.CompatibleModel, this.Options.CompatibleBaseUrl);

        private static string? Unprotect(string? value) {
            if (string.IsNullOrEmpty(value)) return null;
            try { return WindowsSecret.Unprotect(value); } catch (Exception) { return null; }
        }

        public static string? Protect(string? key) => string.IsNullOrWhiteSpace(key) ? null : WindowsSecret.Protect(key.Trim());

        public string KeyOf(ServerMessage message) => message.MessageId ?? message.LocalStorageId ?? this.unnamed.GetValue(message, _ => Guid.NewGuid().ToString("N"));

        public TranslationState? Translation(ServerMessage message) => this.translations.TryGetValue(this.KeyOf(message), out var state) ? state : null;

        public async Task TranslateAsync(ServerMessage message, string source) {
            if (this.Translation(message)?.Status == TranslationStatus.Pending) return;
            this.Set(message, new TranslationState(TranslationStatus.Pending, ""));
            try {
                var display = this.app.Presentation.Engine.Snapshot();
                var text = display.Enabled ? display.Content(source, message) : message.ContentText;
                var client = this.ClientFactory(this.Settings());
                var result = await client.CompleteAsync(AiPrompts.Translation(text, this.TargetLanguage));
                this.Set(message, new TranslationState(TranslationStatus.Done, result));
            } catch (AiException ex) {
                this.Set(message, new TranslationState(TranslationStatus.Failed, Describe(ex)));
            } catch (Exception ex) when (ex is not OutOfMemoryException) {
                this.Set(message, new TranslationState(TranslationStatus.Failed, ex.Message));
            }
        }

        public void ClearTranslation(ServerMessage message) {
            this.translations.Remove(this.KeyOf(message));
            this.TranslationChanged?.Invoke(message);
        }

        private void Set(ServerMessage message, TranslationState state) {
            var key = this.KeyOf(message);
            if (!this.translations.ContainsKey(key)) {
                this.translationOrder.Enqueue(key);
                while (this.translationOrder.Count > MaxTranslations) this.translations.Remove(this.translationOrder.Dequeue());
            }
            this.translations[key] = state;
            this.app.Dispatch(() => this.TranslationChanged?.Invoke(message));
        }

        /// <summary>Summarizes the messages an export of <paramref name="query"/> would contain, in the same line form.</summary>
        public async Task<string> SummarizeAsync(HistoryStore store, HistoryQuery query, Func<ServerMessage, bool>? filter, string scope, CancellationToken token) {
            var display = this.app.Presentation.Engine.Snapshot();
            var lines = new List<string>();
            var collector = new LineCollector(lines);
            // Throws AiException(TooLarge) from the collector once the range is too big, which also stops the read.
            await store.ExportAsync(query, collector, ExportFormat.Text, true, filter, null, token,
                (row, time) => display.ExportLine(row.Source, row.Message, time, ChannelLabels.For(row.Message, !display.Enabled)),
                display.Enabled ? row => display.Observe(row.Source, row.Message) : null);
            if (lines.Count == 0) throw new AiException(AiFailure.TooLarge, LocalizationHelper.GetString("Ai.NothingToSummarize"));
            var client = this.ClientFactory(this.Settings());
            return await client.CompleteAsync(AiPrompts.Summary(lines, scope, this.TargetLanguage), token);
        }

        /// <summary>Collects export lines and stops the read as soon as the summary limit is passed.</summary>
        private sealed class LineCollector(List<string> lines) : TextWriter {
            private readonly StringBuilder current = new();
            private long characters;
            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char value) {
                if (value == '\r') return;
                if (value != '\n') { this.current.Append(value); return; }
                lines.Add(this.current.ToString()); this.characters += this.current.Length + 1; this.current.Clear();
                if (this.characters > AiPrompts.MaxSummaryCharacters)
                    throw new AiException(AiFailure.TooLarge, LocalizationHelper.GetString("Ai.TooLarge"));
            }
            public override void Write(string? value) { if (value != null) foreach (var ch in value) this.Write(ch); }
        }

        public static string Describe(AiException ex) => ex.Failure switch {
            AiFailure.NotConfigured => LocalizationHelper.GetString("Ai.NotConfigured"),
            AiFailure.Authentication => LocalizationHelper.GetString("Ai.Authentication"),
            AiFailure.RateLimited => LocalizationHelper.GetString("Ai.RateLimited"),
            AiFailure.Refused => LocalizationHelper.GetString("Ai.Refused"),
            AiFailure.TooLarge => ex.Message,
            AiFailure.Network => LocalizationHelper.GetString("Ai.Network") + " " + ex.Message,
            _ => LocalizationHelper.GetString("Ai.Failed") + " " + ex.Message,
        };
    }
}
