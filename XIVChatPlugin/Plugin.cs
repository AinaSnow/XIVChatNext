using Dalamud.Game.Command;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Plugin.Services;
#if DEBUG
using System.IO;
#endif

namespace XIVChatPlugin {
    internal class Plugin : IDalamudPlugin {
        internal static string Name => "XIVChat";

        private bool _disposedValue;

        [PluginService]
        internal static IPluginLog Log { get; private set; } = null!;

        [PluginService]
        internal IDalamudPluginInterface Interface { get; private init; } = null!;

        [PluginService]
        internal IChatGui ChatGui { get; private init; } = null!;

        [PluginService]
        internal IClientState ClientState { get; private init; } = null!;

        [PluginService]
        private ICommandManager CommandManager { get; init; } = null!;

        [PluginService]
        internal IDataManager DataManager { get; private init; } = null!;

        [PluginService]
        private IFramework Framework { get; init; } = null!;

        [PluginService]
        internal IObjectTable ObjectTable { get; private init; } = null!;

        [PluginService]
        internal IGameInteropProvider GameInteropProvider { get; private init; } = null!;

        [PluginService]
        private ISigScanner SigScanner { get; init; } = null!;

        internal Configuration Config { get; }
        private PluginUi Ui { get; }
        internal Server Server { get; private set; }
        internal Relay? Relay { get; private set; }
        internal GameFunctions Functions { get; }
        internal InternalEvents Events { get; }
        private List<IDisposable> Ipcs { get; } = [];

        // ReSharper disable once UnusedMember.Global
        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Local
        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        // ReSharper disable once MemberCanBePrivate.Global
        internal string Location { get; private set; } = Assembly.GetExecutingAssembly().Location;

        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        private void SetLocation(string path) {
            this.Location = path;
        }

        public Plugin() {
            this.Events = new InternalEvents();

            // load libsodium.so from debug location if in debug mode
            #if DEBUG
            string path = Environment.GetEnvironmentVariable("PATH")!;
            string newPath = Path.GetDirectoryName(this.Location)!;
            Environment.SetEnvironmentVariable("PATH", $"{path};{newPath}");
            #endif

            this.Config = this.Interface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Config.Initialise(this);

            this.Functions = new GameFunctions(this);

            this.Ui = new PluginUi(this);

            this.LaunchServer();

            if (this.Config.AllowRelayConnections) {
                this.StartRelay();
            }

            this.Interface.UiBuilder.Draw += this.Ui.Draw;
            this.Interface.UiBuilder.OpenConfigUi += this.Ui.OpenSettings;
            this.Framework.Update += this.Server!.OnFrameworkUpdate;
            this.ChatGui.ChatMessage += this.Server.OnChat;
            this.ClientState.Login += this.Server.OnLogIn;
            this.ClientState.Logout += this.Server.OnLogOut;
            this.ClientState.TerritoryChanged += this.Server.OnTerritoryChange;
            this.CommandManager.AddHandler("/xivchat", new CommandInfo(this.OnCommand) {
                HelpMessage = "Opens the config for the XIVChat plugin",
            });

            this.Ipcs.Add(new Ipc.PeepingTom(this));
        }

        public void Dispose() {
            if (this._disposedValue) {
                return;
            }

            this._disposedValue = true;

            this.Relay?.Dispose();
            this.Server.Dispose();

            this.Interface.UiBuilder.Draw -= this.Ui.Draw;
            this.Interface.UiBuilder.OpenConfigUi -= this.Ui.OpenSettings;
            this.Framework.Update -= this.Server.OnFrameworkUpdate;
            this.ChatGui.ChatMessage -= this.Server.OnChat;
            this.ClientState.Login -= this.Server.OnLogIn;
            this.ClientState.Logout -= this.Server.OnLogOut;
            this.ClientState.TerritoryChanged -= this.Server.OnTerritoryChange;
            this.CommandManager.RemoveHandler("/xivchat");
            this.Functions.Dispose();

            foreach (var ipc in this.Ipcs) {
                ipc.Dispose();
            }
        }

        internal void StartRelay() {
            if (this.Relay != null) {
                return;
            }

            this.Relay = new Relay(this);
            this.Relay.Start();
        }

        internal void StopRelay() {
            if (this.Relay == null) {
                return;
            }

            this.Relay.Dispose();
            this.Relay = null;
        }

        internal nint ScanText(string sig) {
            try {
                return this.SigScanner.ScanText(sig);
            } catch (KeyNotFoundException) {
                return nint.Zero;
            }
        }

        internal nint GetStaticAddressFromSig(string sig) {
            try {
                return this.SigScanner.GetStaticAddressFromSig(sig);
            } catch (KeyNotFoundException) {
                return nint.Zero;
            }
        }

        private void LaunchServer() {
            this.Server = new Server(this);
            this.Server.Spawn();
        }

        internal void RelaunchServer() {
            this.Server.Dispose();
            this.LaunchServer();
        }

        private void OnCommand(string command, string args) {
            this.Ui.OpenSettings();
        }
    }
}
