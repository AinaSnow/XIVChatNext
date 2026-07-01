using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using WebSocketSharp;
using XIVChatCommon.Message.Relay;

namespace XIVChatPlugin {
    internal enum ConnectionStatus {
        Disconnected,
        Connecting,
        Negotiating,
        Connected,
    }

    internal class Relay : IDisposable {
        #if DEBUG
        private static readonly Uri RelayUrl = new("ws://localhost:14555/", UriKind.Absolute);
        #else
        private static readonly Uri RelayUrl = new("wss://relay.xiv.chat/", UriKind.Absolute);
        #endif

        internal static string? ConnectionError { get; private set; }

        private bool Disposed { get; set; }

        private Plugin Plugin { get; }

        private WebSocket Connection { get; }

        private bool Running { get; set; }

        internal ConnectionStatus Status { get; private set; }

        private Channel<IToRelay> ToRelay { get; } = Channel.CreateUnbounded<IToRelay>();

        internal Relay(Plugin plugin) {
            this.Plugin = plugin;

            this.Connection = new WebSocket(RelayUrl.ToString()) {
                SslConfiguration = {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                },
            };

            this.Connection.OnOpen += this.OnOpen;
            this.Connection.OnMessage += this.OnMessage;
            this.Connection.OnClose += this.OnClose;
            this.Connection.OnError += this.OnError;
        }

        public void Dispose() {
            this.Disposed = true;
            new Thread(() => this.Connection.Close(CloseStatusCode.Normal)).Start();
            this.Running = false;
        }

        internal void Start() {
            if (this.Plugin.Config.RelayAuth == null) {
                return;
            }

            this.Running = true;

            this.Status = ConnectionStatus.Connecting;
            new Thread(() => this.Connection.Connect()).Start();
        }

        internal void ResendPublicKey() {
            var keys = this.Plugin.Config.KeyPair;
            if (keys == null) {
                return;
            }

            var msg = new RelayRegister {
                AuthToken = "",
                PublicKey = keys.PublicKey,
            };
            var bytes = MessagePackSerializer.Serialize((IToRelay) msg);

            this.Connection.Send(bytes);
        }

        internal void DisconnectClient(IEnumerable<byte> pk) {
            var msg = new RelayClientDisconnect {
                PublicKey = pk.ToList(),
            };
            var bytes = MessagePackSerializer.Serialize((IToRelay) msg);

            this.Connection.Send(bytes);
        }

        private void OnOpen(object? o, EventArgs eventArgs) {
            this.Status = ConnectionStatus.Negotiating;

            var auth = this.Plugin.Config.RelayAuth;
            if (auth == null) {
                return;
            }

            var keys = this.Plugin.Config.KeyPair;
            if (keys == null) {
                return;
            }

            var message = new RelayRegister {
                AuthToken = auth,
                PublicKey = keys.PublicKey,
            };
            var bytes = MessagePackSerializer.Serialize((IToRelay) message);

            this.Connection.Send(bytes);

            Task.Run(async () => {
                while (this.Running) {
                    this.Connection.Ping();
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }
            });

            Task.Run(async () => {
                while (this.Running) {
                    var message = await this.ToRelay.Reader.ReadAsync();
                    var bytes = MessagePackSerializer.Serialize(message);

                    this.Connection.Send(bytes);
                }
            });
        }

        private void OnMessage(object? sender, MessageEventArgs args) {
            var message = MessagePackSerializer.Deserialize<IFromRelay>(args.RawData);
            switch (message) {
                case RelaySuccess success:
                    if (success.Success) {
                        ConnectionError = null;
                        this.Status = ConnectionStatus.Connected;
                    } else {
                        Plugin.Log.Warning($"Relay: {success.Info}");
                        ConnectionError = success.Info;
                        this.Status = ConnectionStatus.Disconnected;
                        this.Plugin.StopRelay();
                    }

                    break;
                case RelayNewClient newClient:
                    #pragma warning disable CA1806
                    IPAddress.TryParse(newClient.Address, out var remote);
                    #pragma warning restore CA1806
                    var client = new RelayConnected(
                        newClient.PublicKey.ToArray(),
                        remote,
                        this.ToRelay.Writer,
                        Channel.CreateUnbounded<byte[]>()
                    );

                    this.Plugin.Server.SpawnClientTask(client, false);
                    break;
                case RelayClientDisconnect disconnect:
                    var clientPk = disconnect.PublicKey.ToArray();
                    var id = this.Plugin.Server.Clients
                        .Where(client => client.Value is RelayConnected)
                        .Where(client => client.Value.Handshake?.RemotePublicKey?.SequenceEqual(clientPk) ?? false)
                        .Select(client => client.Key)
                        .FirstOrDefault();
                    if (id != default) {
                        this.Plugin.Server.RemoveClient(id);
                    }

                    break;
                case RelayedMessage relayed:
                    var relayedClient = this.Plugin.Server.Clients.Values
                        .Where(client => client is RelayConnected)
                        .Cast<RelayConnected>()
                        .FirstOrDefault(client => client.PublicKey.SequenceEqual(relayed.PublicKey));

                    relayedClient?.FromRelayWriter.WriteAsync(relayed.Message.ToArray()).AsTask().Wait();
                    break;
            }
        }

        private void OnClose(object? sender, CloseEventArgs args) {
            this.Running = false;
            this.Status = ConnectionStatus.Disconnected;

            if (!args.WasClean && !this.Disposed) {
                Task.Run(async () => await Task.Delay(3_000)).ContinueWith(_ => this.Start());
            }
        }

        private void OnError(object? sender, ErrorEventArgs args) {
            Plugin.Log.Error(args.Exception, $"Error in relay connection: {args.Message}");
            this.Running = false;
            this.Status = ConnectionStatus.Disconnected;

            if (!this.Disposed) {
                Task.Run(async () => await Task.Delay(3_000)).ContinueWith(_ => this.Start());
            }
        }
    }
}
