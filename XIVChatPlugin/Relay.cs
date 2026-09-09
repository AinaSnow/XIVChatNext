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
        private readonly Server server;

        private WebSocket Connection { get; }

        private bool Running { get; set; }

        internal ConnectionStatus Status { get; private set; }

        private sealed record Outbound(IToRelay Message, CancellationToken Cancellation, TaskCompletionSource Completion);
        private Channel<Outbound> ToRelay { get; } = Channel.CreateBounded<Outbound>(32);
        private readonly CancellationTokenSource lifetime = new();
        private CancellationTokenSource? connectionLifetime;
        private int reconnectScheduled;

        private async Task SendAsync(IToRelay message, CancellationToken token) {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, this.lifetime.Token);
            await this.ToRelay.Writer.WriteAsync(new Outbound(message, token, completion), linked.Token);
            await completion.Task.WaitAsync(linked.Token);
        }

        internal Relay(Plugin plugin) {
            this.Plugin = plugin;
            this.server = plugin.Server;

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
            this.lifetime.Cancel();
            this.connectionLifetime?.Cancel();
            this.ToRelay.Writer.TryComplete();
            while (this.ToRelay.Reader.TryRead(out var pending)) pending.Completion.TrySetCanceled();
            this.DisconnectRelayClients();
            _ = Task.Run(() => this.Connection.Close(CloseStatusCode.Normal));
            this.Running = false;
        }

        internal void Start() {
            if (this.Disposed || this.Plugin.Config.RelayAuth == null) {
                return;
            }

            this.Running = true;

            this.Status = ConnectionStatus.Connecting;
            _ = Task.Run(() => this.Connection.Connect());
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
            this.QueueControl(msg);
        }

        internal void DisconnectClient(IEnumerable<byte> pk) {
            var msg = new RelayClientDisconnect {
                PublicKey = pk.ToList(),
            };
            this.QueueControl(msg);
        }

        private void QueueControl(IToRelay message) {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!this.ToRelay.Writer.TryWrite(new Outbound(message, this.lifetime.Token, completion))) {
                ConnectionError = "Relay control queue is full or closed.";
                return;
            }
            _ = completion.Task.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void OnOpen(object? o, EventArgs eventArgs) {
            if (this.Disposed) return;
            this.connectionLifetime?.Cancel();
            this.connectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(this.lifetime.Token);
            var connectionToken = this.connectionLifetime.Token;
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

            _ = Task.Run(async () => {
                try {
                    while (!connectionToken.IsCancellationRequested) {
                        await Task.Delay(TimeSpan.FromSeconds(30), connectionToken);
                        this.Connection.Ping();
                    }
                } catch (OperationCanceledException) { }
            });
            Task.Run(async () => {
                try {
                    while (!connectionToken.IsCancellationRequested) {
                        var pending = await this.ToRelay.Reader.ReadAsync(connectionToken);
                        try {
                            pending.Cancellation.ThrowIfCancellationRequested();
                            connectionToken.ThrowIfCancellationRequested();
                            var encoded = MessagePackSerializer.Serialize(pending.Message);
                            if (encoded.Length > 256_000) throw new InvalidOperationException("Relay envelope exceeds its byte budget.");
                            this.Connection.Send(encoded);
                            pending.Completion.TrySetResult();
                        } catch (Exception ex) { pending.Completion.TrySetException(ex); }
                    }
                } catch (OperationCanceledException) { }
                catch (ChannelClosedException) { }
            });
        }

        private void OnMessage(object? sender, MessageEventArgs args) {
            if (this.Disposed || args.RawData.Length > 256_000) return;
            IFromRelay message;
            try { message = MessagePackSerializer.Deserialize<IFromRelay>(args.RawData); }
            catch (MessagePackSerializationException) { ConnectionError = "Malformed relay packet."; return; }
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
                    if (newClient.PublicKey.Count != 32) break;
                    #pragma warning disable CA1806
                    IPAddress.TryParse(newClient.Address, out var remote);
                    #pragma warning restore CA1806
                    var client = new RelayConnected(
                        newClient.PublicKey.ToArray(),
                        remote,
                        this.SendAsync
                    );

                    this.server.SpawnClientTask(client, false);
                    break;
                case RelayClientDisconnect disconnect:
                    var clientPk = disconnect.PublicKey.ToArray();
                    var id = this.server.Clients
                        .Where(client => client.Value is RelayConnected)
                        .Where(client => client.Value.Handshake?.RemotePublicKey?.SequenceEqual(clientPk) ?? false)
                        .Select(client => client.Key)
                        .FirstOrDefault();
                    if (id != default) {
                        this.server.RemoveClient(id);
                    }

                    break;
                case RelayedMessage relayed:
                    var relayedClient = this.server.Clients.Values
                        .Where(client => client is RelayConnected)
                        .Cast<RelayConnected>()
                        .FirstOrDefault(client => client.PublicKey.SequenceEqual(relayed.PublicKey));

                    relayedClient?.Receive(relayed.Message.ToArray());
                    break;
            }
        }

        private void OnClose(object? sender, CloseEventArgs args) {
            this.Running = false;
            this.connectionLifetime?.Cancel();
            this.DisconnectRelayClients();
            this.Status = ConnectionStatus.Disconnected;

            if (!args.WasClean && !this.Disposed) {
                this.ScheduleReconnect();
            }
        }

        private void OnError(object? sender, ErrorEventArgs args) {
            ConnectionError = args.Message;
            Plugin.Log.Error(args.Exception, $"Error in relay connection: {args.Message}");
            this.Running = false;
            this.connectionLifetime?.Cancel();
            this.DisconnectRelayClients();
            this.Status = ConnectionStatus.Disconnected;

            if (!this.Disposed) {
                this.ScheduleReconnect();
            }
        }

        private void DisconnectRelayClients() {
            foreach (var pair in this.server.Clients.Where(c => c.Value is RelayConnected).ToArray())
                this.server.RemoveClient(pair.Key);
        }
        private void ScheduleReconnect() {
            if (Interlocked.Exchange(ref this.reconnectScheduled, 1) != 0) return;
            _ = Task.Run(async () => {
                try { await Task.Delay(3_000, this.lifetime.Token); this.Start(); }
                catch (OperationCanceledException) { }
                finally { Interlocked.Exchange(ref this.reconnectScheduled, 0); }
            });
        }
    }
}
