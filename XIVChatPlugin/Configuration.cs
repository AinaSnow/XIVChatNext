using Dalamud.Configuration;
using Sodium;
using System;
using System.Collections.Generic;

namespace XIVChatPlugin {
    [Serializable]
    internal class Configuration : IPluginConfiguration {
        private Plugin? _plugin;

        public int Version { get; set; } = 1;
        public ushort Port { get; set; } = 14777;

        public bool BacklogEnabled { get; set; } = true;
        public ushort BacklogCount { get; set; } = 100;

        public bool SendBattle { get; set; } = true;

        public bool MessagesCountAsInput { get; set; } = true;

        public bool AcceptNewClients { get; set; } = true;

        public bool AllowRelayConnections { get; set; }
        public string? RelayAuth { get; set; }

        public Dictionary<Guid, Tuple<string, byte[]>> TrustedKeys { get; set; } = new();
        public KeyPair? KeyPair { get; set; }

        internal void Initialise(Plugin plugin) {
            this._plugin = plugin;
        }

        internal void Save() {
            this._plugin?.Interface.SavePluginConfig(this);
        }
    }
}
