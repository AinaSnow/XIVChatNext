using Newtonsoft.Json;
using Sodium;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop {
    [JsonObject]
    public class Configuration : INotifyPropertyChanged {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? Saved;
        [JsonIgnore] internal string? FilePathOverride { get; set; }

        public string? LicenceKey { get; set; }

        [JsonIgnore] public KeyPair KeyPair { get; set; } = PublicKeyBox.GenerateKeyPair();

        // On disk the public key stays readable and the private key is protected for the current Windows user.
        // Files written before 1.5.0 stored the whole pair in plain text under "KeyPair"; it is read once and never written.
        [JsonProperty("KeyPair", NullValueHandling = NullValueHandling.Ignore)]
        private KeyPair? LegacyKeyPair { get => null; set => this.legacyKeyPair = value; }
        [JsonProperty("PublicKey")]
        private byte[]? StoredPublicKey { get => this.KeyPair.PublicKey; set => this.storedPublicKey = value; }
        [JsonProperty("ProtectedPrivateKey")]
        private string? StoredPrivateKey { get => this.ProtectPrivateKey(); set => this.storedPrivateKey = value; }
        private KeyPair? legacyKeyPair, protectedFor;
        private byte[]? storedPublicKey;
        private string? storedPrivateKey;
        private string? protectedPrivateKey;
        /// <summary>The file held a plain-text key that the next save replaces, including in the backup.</summary>
        [JsonIgnore] internal bool MigratedPlainTextKey { get; private set; }
        /// <summary>The saved key could not be decrypted on this Windows account, so a new identity was created.</summary>
        [JsonIgnore] internal bool IdentityReset { get; private set; }

        private string ProtectPrivateKey() {
            if (!ReferenceEquals(this.protectedFor, this.KeyPair) || this.protectedPrivateKey == null) {
                this.protectedPrivateKey = XIVChat.Relay.Transport.WindowsSecret.Protect(Convert.ToBase64String(this.KeyPair.PrivateKey));
                this.protectedFor = this.KeyPair;
            }
            return this.protectedPrivateKey;
        }

        [OnDeserialized]
        private void RestoreKeyPair(StreamingContext context) {
            if (this.storedPublicKey != null && this.storedPrivateKey != null) {
                try {
                    var privateKey = Convert.FromBase64String(XIVChat.Relay.Transport.WindowsSecret.Unprotect(this.storedPrivateKey));
                    this.KeyPair = new KeyPair(this.storedPublicKey, privateKey);
                    this.protectedFor = this.KeyPair;
                    this.protectedPrivateKey = this.storedPrivateKey;
                } catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) {
                    // Copied from another Windows account or machine. Keep settings and history; devices must be trusted again.
                    this.IdentityReset = true;
                }
            } else if (this.legacyKeyPair != null) {
                this.KeyPair = this.legacyKeyPair;
                this.MigratedPlainTextKey = true;
            } else {
                throw new InvalidDataException("Configuration is missing the device key.");
            }
            this.legacyKeyPair = null; this.storedPublicKey = null; this.storedPrivateKey = null;
        }

        public ObservableCollection<SavedServer> Servers { get; set; } = new ObservableCollection<SavedServer>();
        public SavedServer? LastSuccessfulConnection { get; set; }
        public int SetupVersion { get; set; }
        public bool SetupInitialized { get; set; }
        public bool SetupDeferred { get; set; }
        internal bool PrepareSetup(bool existing, bool recovered) {
            if (recovered) return false;
            if (!SetupInitialized) { SetupInitialized = true; if (existing) SetupVersion = 1; }
            return SetupVersion == 0 && !SetupDeferred;
        }
        public HashSet<TrustedKey> TrustedKeys { get; set; } = new HashSet<TrustedKey>();

        public ObservableCollection<Tab> Tabs { get; set; } = Tab.Defaults();

        public bool AlwaysOnTop { get; set; }

        /// <summary>Closing the main window keeps the app running in the notification area.</summary>
        public bool CloseToTray { get; set; }
        public bool GlobalHotkeyEnabled { get; set; }
        public uint GlobalHotkeyModifiers { get; set; } = ShellIntegration.ModControl | ShellIntegration.ModAlt;
        public uint GlobalHotkeyKey { get; set; } = 0x58; // X

        private double fontSize = 14d;

        public double FontSize {
            get => this.fontSize;
            set {
                if (Math.Abs(this.fontSize - value) < 0.001) return;
                this.fontSize = value;
                this.OnPropertyChanged(nameof(this.FontSize));
            }
        }

        public ushort BacklogMessages { get; set; } = 500;

        public uint LocalBacklogMessages { get; set; } = 10_000;

        private bool historyEnabled = true;
        public bool HistoryEnabled {
            get => this.historyEnabled;
            set { if (this.historyEnabled == value) return; this.historyEnabled = value; this.OnPropertyChanged(nameof(this.HistoryEnabled)); }
        }
        // Zero means keep forever. Turning history off stops new persistence.
        public int HistoryRetentionDays { get; set; } = 90;
        public bool OnlineAvatars { get; set; } = true;
        public bool CheckForUpdatesOnStartup { get; set; } = true;
        public XIVChatCommon.Presentation.PrivacySettings Privacy { get; set; } = new();

        private double opacity = 1.0;

        public double Opacity {
            get => this.opacity;
            set {
                if (Math.Abs(this.opacity - value) < 0.001) return;
                this.opacity = value;
                this.OnPropertyChanged(nameof(this.Opacity));
            }
        }

        private bool compactMode;

        public bool CompactMode {
            get => this.compactMode;
            set {
                if (this.compactMode == value) return;
                this.compactMode = value;
                this.OnPropertyChanged(nameof(this.CompactMode));
            }
        }

        private Theme theme = Theme.System;

        public Theme Theme {
            get => this.theme;
            set {
                if (this.theme == value) return;
                this.theme = value;
                this.OnPropertyChanged(nameof(this.Theme));
            }
        }

        private AppLanguage language = AppLanguage.System;

        public AppLanguage Language {
            get => this.language;
            set {
                if (this.language == value) return;
                this.language = value;
                this.OnPropertyChanged(nameof(this.Language));
            }
        }

        public ObservableCollection<Notification> Notifications { get; set; } = new ObservableCollection<Notification>();
        public NotificationOptions NotificationOptions { get; set; } = new();

        private void OnPropertyChanged(string propName) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        #region io

        private static string FilePath() => Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVChat for Windows",
            "config.json"
        );

        internal static string ConfigFilePath => FilePath();

        public static Configuration? Load(out bool usedBackup) {
            return ConfigurationFile.Load(FilePath(), Deserialize, out usedBackup);
        }

        private static Configuration Deserialize(string contents) {
            try {
                var config = JsonConvert.DeserializeObject<Configuration>(contents, new JsonSerializerSettings {
                    ObjectCreationHandling = ObjectCreationHandling.Replace,
                    CheckAdditionalContent = true,
                });
                if (config?.KeyPair?.PublicKey?.Length != 32 || config.KeyPair.PrivateKey?.Length != 32
                    || config.Servers == null || config.TrustedKeys == null || config.Tabs == null || config.Notifications == null
                    || config.Tabs.Any(tab => tab == null || tab.Filter?.Types == null)) {
                    throw new InvalidDataException("Configuration is missing required keys or collections.");
                }
                config.NotificationOptions ??= new NotificationOptions();
                config.Privacy ??= new XIVChatCommon.Presentation.PrivacySettings();
                config.Privacy.Validate();
                if (!config.NotificationOptions.ValidTimes || config.Notifications.Any(rule => rule == null || rule.Channels == null || rule.Substrings == null))
                    throw new InvalidDataException("Notification settings are invalid.");
                var tabIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var tab in config.Tabs)
                    if (string.IsNullOrWhiteSpace(tab.Id) || !tabIds.Add(tab.Id)) { tab.Id = Guid.NewGuid().ToString("N"); tabIds.Add(tab.Id); }
                var serverIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var server in config.Servers) {
                    if (server == null) throw new InvalidDataException("Invalid saved connection.");
                    if (string.IsNullOrWhiteSpace(server.Id) || !serverIds.Add(server.Id)) { server.Id = Guid.NewGuid().ToString("N"); serverIds.Add(server.Id); }
                }
                return config;
            } catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException) {
                throw new InvalidDataException("Configuration JSON is invalid.", ex);
            }
        }

        public void Save() {
            var contents = JsonConvert.SerializeObject(this, Formatting.Indented);
            ConfigurationFile.Save(FilePathOverride ?? FilePath(), contents, text => { _ = Deserialize(text); });
            if (this.MigratedPlainTextKey) {
                // The first save rotates the plain-text file into the backup; a second save replaces that backup too.
                ConfigurationFile.Save(FilePathOverride ?? FilePath(), contents, text => { _ = Deserialize(text); });
                this.MigratedPlainTextKey = false;
            }
            this.Saved?.Invoke();
        }

        #endregion
    }

    [JsonObject]
    public class SavedServer : INotifyPropertyChanged {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        private XIVChat.Relay.Protocol.RelayProfile? relay;
        public XIVChat.Relay.Protocol.RelayProfile? Relay { get => relay; set { relay = value; OnPropertyChanged(nameof(Relay)); OnPropertyChanged(nameof(Description)); } }
        [JsonIgnore] public string Description => Relay == null ? Host + ":" + Port : Relay.Server + " · Relay";
        public SavedServer Snapshot() => new(Name, Host, Port) { Id = Id, Relay = Relay };
        private string name;
        private string host;
        private ushort port;

        public string Name {
            get => this.name;
            set {
                this.name = value;
                this.OnPropertyChanged(nameof(this.Name));
            }
        }

        public string Host {
            get => this.host;
            set {
                this.host = value;
                this.OnPropertyChanged(nameof(this.Host));
                this.OnPropertyChanged(nameof(this.Description));
            }
        }

        public ushort Port {
            get => this.port;
            set {
                this.port = value;
                this.OnPropertyChanged(nameof(this.Port));
                this.OnPropertyChanged(nameof(this.Description));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public SavedServer(string name, string host, ushort port) {
            this.name = name;
            this.host = host;
            this.port = port;
        }

        protected bool Equals(SavedServer other) {
            return this.Id == other.Id;
        }

        public override bool Equals(object? obj) {
            if (obj is null) {
                return false;
            }

            if (ReferenceEquals(this, obj)) {
                return true;
            }

            return obj.GetType() == this.GetType() && this.Equals((SavedServer)obj);
        }

        [SuppressMessage("ReSharper", "NonReadonlyMemberInGetHashCode")]
        public override int GetHashCode() {
            return this.Id.GetHashCode();
        }
    }

    public enum Theme {
        System,
        Light,
        Dark,
    }

    [JsonObject]
    public class TrustedKey {
        public string Name { get; set; }
        public byte[] Key { get; set; }

        public TrustedKey(string name, byte[] key) {
            this.Name = name;
            this.Key = key;
        }
    }

    [JsonObject]
    public class Tab : IEnumerable<ServerMessage>, INotifyCollectionChanged, INotifyPropertyChanged {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        private string name;
        private bool processMarkdown;

        public Tab(string name) {
            this.name = name;
        }

        public string Name {
            get => this.name;
            set {
                this.name = value;
                this.OnPropertyChanged(nameof(this.Name));
            }
        }

        public Filter Filter { get; set; } = new Filter();

        public bool ProcessMarkdown {
            get => this.processMarkdown;
            set {
                this.processMarkdown = value;
                this.OnPropertyChanged(nameof(this.ProcessMarkdown));
            }
        }

        private double fontSizeOverride;
        public double FontSizeOverride {
            get => fontSizeOverride;
            set { fontSizeOverride = value; OnPropertyChanged(nameof(FontSizeOverride)); }
        }

        private bool showTimestamps = true;
        public bool ShowTimestamps {
            get => showTimestamps;
            set { showTimestamps = value; OnPropertyChanged(nameof(ShowTimestamps)); }
        }

        [JsonIgnore]
        public List<ServerMessage> Messages { get; } = new List<ServerMessage>();

        private void NotifyReset() {
            this.CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        private void NotifyAdd(ServerMessage message) {
            this.CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, message));
        }

        private void NotifyAddItemsAt(IList messages, int index) {
            this.CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, messages, index));
        }

        private void NotifyRemoveItemsAt(IList messages, int index) {
            this.CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, messages, index));
        }

        public void RepopulateMessages(IEnumerable<ServerMessage> mainMessages) {
            this.Messages.Clear();

            // add messages from newest to oldest
            foreach (var message in mainMessages.Where(msg => this.Filter.Allowed(msg))) {
                this.Messages.Add(message);
            }

            this.NotifyReset();
        }

        private int lastSequence = -1;
        private int insertAt;

        public void AddReversedChunk(ServerMessage[] messages, int sequence, Configuration config) {
            if (sequence != this.lastSequence) {
                this.lastSequence = sequence;
                this.insertAt = this.Messages.Count;
            }

            var filtered = messages
                .Where(msg => msg.Channel == 0 || this.Filter.Allowed(msg))
                .ToList();

            this.Messages.InsertRange(this.insertAt, filtered);
            this.NotifyAddItemsAt(filtered, this.insertAt);

            this.Prune(config);
        }

        public void AddMessage(ServerMessage message, Configuration config) {
            if (message.Channel != 0 && !this.Filter.Allowed(message)) {
                return;
            }

            this.Messages.Add(message);
            this.NotifyAdd(message);

            this.Prune(config);
        }

        public void MergeHistory(IEnumerable<ServerMessage> messages, Configuration config) {
            foreach (var message in messages.Where(message => this.Filter.Allowed(message))) {
                var index = this.Messages.FindIndex(existing => ChatSession.Compare(message, existing) < 0);
                if (index < 0) index = this.Messages.Count;
                this.Messages.Insert(index, message);
                this.NotifyAddItemsAt(new[] { message }, index);
            }
            this.Prune(config);
        }

        private void Prune(Configuration config) {
            var diff = this.Messages.Count - config.LocalBacklogMessages;
            if (diff <= 0) {
                return;
            }

            var removed = this.Messages.Take((int)diff).ToList();
            this.Messages.RemoveRange(0, (int)diff);
            this.insertAt = Math.Max(0, this.insertAt - (int)diff);
            this.NotifyRemoveItemsAt(removed, 0);
        }

        public void ClearMessages() {
            this.Messages.Clear();
            this.NotifyReset();
        }

        public static Filter GeneralFilter() {
            var generalFilters = FilterCategory.Chat.Types()
                .Concat(FilterCategory.Announcements.Types())
                .ToHashSet();
            generalFilters.Remove(FilterType.OwnBattleSystem);
            generalFilters.Remove(FilterType.OthersBattleSystem);
            generalFilters.Remove(FilterType.NpcDialogue);
            generalFilters.Remove(FilterType.OthersFishing);
            return new Filter {
                Types = generalFilters,
            };
        }

        public static ObservableCollection<Tab> Defaults() {
            var battleFilters = FilterCategory.Battle.Types()
                .Append(FilterType.OwnBattleSystem)
                .Append(FilterType.OthersBattleSystem)
                .ToHashSet();

            return new ObservableCollection<Tab> {
                new Tab("General") {
                    Filter = GeneralFilter(),
                },
                new Tab("Battle") {
                    Filter = new Filter {
                        Types = battleFilters,
                    },
                },
            };
        }

        public IEnumerator<ServerMessage> GetEnumerator() {
            return this.Messages.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return this.GetEnumerator();
        }

        public event NotifyCollectionChangedEventHandler? CollectionChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    [JsonObject]
    public class Filter {
        public HashSet<FilterType> Types { get; set; } = new HashSet<FilterType>();

        public virtual bool Allowed(ServerMessage message) {
            var code = new ChatCode((ushort)message.Channel);
            return this.Types.Any(type => type.Allowed(code));
        }
    }

    [JsonObject]
    public class Notification {
        public string Name { get; set; }
        public bool MatchAll { get; set; }
        public List<ChatType> Channels { get; set; } = new List<ChatType>();
        public List<string> Substrings { get; set; } = new List<string>();

        private IReadOnlyCollection<String> regexes = new List<string>();

        public IReadOnlyCollection<string> Regexes {
            get => this.regexes;
            set {
                this.regexes = value ?? Array.Empty<string>();
                this.ResetRegexes();
            }
        }

        [JsonIgnore]
        public Lazy<List<Regex>> ParsedRegexes { get; private set; } = null!;

        public Notification(string name) {
            this.Name = name;
            this.ResetRegexes();
        }

        private void ResetRegexes() {
            this.ParsedRegexes = new Lazy<List<Regex>>(
                () => {
                    try {
                        return this.ParseRegexes();
                    } catch (ArgumentException) {
                        return new List<Regex>();
                    }
                }
            );
        }

        private List<Regex> ParseRegexes() {
            return this.Regexes
                .Take(64).Select(regex => new Regex(regex, RegexOptions.Compiled, TimeSpan.FromMilliseconds(50)))
                .ToList();
        }

        [SuppressMessage("ReSharper", "ConvertIfStatementToReturnStatement")]
        public bool Matches(ServerMessage message) {
            if (!this.Channels.Any(channel => ((ushort)channel & 127) == ((ushort)message.Channel & 127))) {
                return false;
            }

            if (this.MatchAll) {
                return true;
            }

            if (this.Substrings.Count == 0 && this.Regexes.Count == 0) {
                return false;
            }

            var text = message.ContentText;

            if (this.Substrings.Any(substring => text.ContainsIgnoreCase(substring))) {
                return true;
            }

            foreach (var regex in this.ParsedRegexes.Value) {
                try { if (regex.IsMatch(text)) return true; }
                catch (RegexMatchTimeoutException) { }
            }

            return false;
        }
    }
}
