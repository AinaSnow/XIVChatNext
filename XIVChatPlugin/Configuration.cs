using Dalamud.Configuration;
using Sodium;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace XIVChatPlugin {
    [Serializable]
    internal class Configuration : IPluginConfiguration {
        private Plugin? _plugin;

        // New configurations start at the current version; saved files keep the version they were written with.
        public int Version { get; set; } = 2;
        public ushort Port { get; set; } = 14777;
        public string ServiceId { get; set; } = Guid.NewGuid().ToString("N");

        public bool BacklogEnabled { get; set; } = true;
        public ushort BacklogCount { get; set; } = 100;
        public int BacklogMaxMiB { get; set; } = 32;
        // Auto, Chinese, English. Plugin UI language is independent of game data language.
        public int UiLanguage { get; set; } = 2;

        public bool SendBattle { get; set; } = true;

        public bool MessagesCountAsInput { get; set; } = true;

        public bool AcceptNewClients { get; set; } = true;

        // Listen on all interfaces instead of loopback only. Defaults off; configurations from before version 2 keep it on.
        public bool AllowLanConnections { get; set; }

        public bool AllowRelayConnections { get; set; }
        public string? RelayAuth { get; set; }
        public string RelayUrl { get; set; } = "";
        public string? RelayCredential { get; set; }
        public string? RelayCertificate { get; set; }

        public ConcurrentDictionary<Guid, Tuple<string, byte[]>> TrustedKeys { get; set; } = new();
        public KeyPair? KeyPair { get; set; }

        internal void Initialise(Plugin plugin) {
            this._plugin = plugin;
            if (this.Version < 2) {
                // Earlier versions always listened on every interface; keep existing LAN setups working.
                this.AllowLanConnections = true;
                this.Version = 2;
            }
            this.BacklogMaxMiB = Math.Clamp(this.BacklogMaxMiB, 1, 256);
        }

        internal void Save() {
            this._plugin?.Interface.SavePluginConfig(this);
        }
    }
}
