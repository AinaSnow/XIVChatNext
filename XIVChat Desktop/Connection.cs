using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;

namespace XIVChat_Desktop {
    public class Connection : INotifyPropertyChanged {
        private readonly App app;

        private readonly string host;
        private readonly ushort port;

        private TcpClient? client;

        private readonly Channel<string> outgoing = Channel.CreateUnbounded<string>();
        private readonly Channel<byte[]> outgoingMessages = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<byte[]> incoming = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<byte> cancelChannel = Channel.CreateBounded<byte>(1);

        public readonly CancellationTokenSource cancel = new CancellationTokenSource();

        public delegate void ReceiveMessageDelegate(ServerMessage message);

        public event ReceiveMessageDelegate? ReceiveMessage;

        public event PropertyChangedEventHandler? PropertyChanged;
        public string? CurrentChannel { get; private set; }

        private bool available;

        public bool Available {
            get => this.available;
            private set {
                this.available = value;
                this.OnPropertyChanged(nameof(this.Available));
            }
        }

        public Connection(App app, string host, ushort port) {
            this.app = app;

            this.host = host;
            this.port = port;
        }

        public void SendMessage(string message) {
            this.outgoing.Writer.TryWrite(message);
        }

        public void ChangeChannel(InputChannel channel) {
            var msg = new ClientChannel {
                Channel = channel,
            };
            this.outgoingMessages.Writer.TryWrite(msg.Encode());
        }

        public void Disconnect() {
            this.cancel.Cancel();
            this.cancelChannel.Writer.TryWrite(1);
        }

        public async Task Connect() {
            Task? receiver = null;
            try {
                this.client = new TcpClient();
                await this.client.ConnectAsync(this.host, this.port, this.cancel.Token);
                var stream = this.client.GetStream();

                // write the magic bytes
                await stream.WriteAsync(new byte[] {
                    14, 20, 67,
                }, this.cancel.Token);

                // do the handshake
                using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(this.cancel.Token);
                handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                var handshake = await KeyExchange.ClientHandshake(this.app.Config.KeyPair, stream, handshakeTimeout.Token);
                handshakeTimeout.CancelAfter(Timeout.InfiniteTimeSpan);

                // check for trust and prompt if not
                if (!this.app.Config.TrustedKeys.Any(trusted => trusted.Key.SequenceEqual(handshake.RemotePublicKey))) {
                    var trustChannel = Channel.CreateBounded<bool>(1);

                    this.DispatchIfCurrent(() => {
                        new TrustDialog(trustChannel.Writer, handshake.RemotePublicKey).Activate();
                    });

                    var trusted = await trustChannel.Reader.ReadAsync(this.cancel.Token);

                    if (!trusted) {
                        goto Close;
                    }
                }

                // clear messages if connecting to a different host
                var currentHost = $"{this.host}:{this.port}";
                var sameHost = this.app.LastHost == currentHost;
                if (!sameHost) {
                    this.DispatchIfCurrent(() => {
                        this.app.Window.ClearAllMessages();
                        this.app.LastHost = currentHost;
                    });
                }

                this.DispatchIfCurrent(() => {
                    this.app.Window.AddSystemMessage("Connected");
                });

                // tell the server our preferences
                var preferences = new ClientPreferences {
                    Preferences = new Dictionary<ClientPreference, object> {
                        {
                            ClientPreference.BacklogNewestMessagesFirst, true
                        },
                    },
                };
                await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, preferences, this.cancel.Token);

                // check if backlog or catch-up is needed
                if (sameHost) {
                    // catch-up
                    var lastRealMessage = this.app.Window.Messages.LastOrDefault(msg => msg.Channel != 0);
                    if (lastRealMessage != null) {
                        _backlogSequence += 1;
                        var catchUp = new ClientCatchUp(lastRealMessage.Timestamp);
                        await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, catchUp, this.cancel.Token);
                    }
                } else if (this.app.Config.BacklogMessages > 0) {
                    // backlog
                    _backlogSequence += 1;
                    var backlogReq = new ClientBacklog {
                        Amount = this.app.Config.BacklogMessages,
                    };
                    await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, backlogReq, this.cancel.Token);
                }

                // start a task for accepting incoming messages and sending them down the channel
                receiver = Task.Run(async () => {
                    try {
                        while (!this.cancel.IsCancellationRequested) {
                            var rawMessage = await SecretMessage.ReadSecretMessage(stream, handshake.Keys.rx, this.cancel.Token);
                            await this.incoming.Writer.WriteAsync(rawMessage, this.cancel.Token);
                        }
                        this.incoming.Writer.TryComplete();
                    } catch (Exception ex) {
                        // Propagate a failed read to the owner instead of leaving it waiting forever.
                        this.incoming.Writer.TryComplete(ex);
                    }
                });

                var incoming = this.incoming.Reader.ReadAsync().AsTask();
                var outgoing = this.outgoing.Reader.ReadAsync().AsTask();
                var outgoingMessage = this.outgoingMessages.Reader.ReadAsync().AsTask();
                var cancel = this.cancelChannel.Reader.ReadAsync().AsTask();

                // listen for incoming and outgoing messages and cancel requests
                while (!this.cancel.IsCancellationRequested) {
                    var result = await Task.WhenAny(incoming, outgoing, outgoingMessage, cancel);
                    if (result == incoming) {
                        var rawMessage = await incoming;
                        incoming = this.incoming.Reader.ReadAsync().AsTask();

                        await this.HandleIncoming(rawMessage);
                    } else if (result == outgoing) {
                        var toSend = await outgoing;
                        outgoing = this.outgoing.Reader.ReadAsync().AsTask();

                        var message = new ClientMessage(toSend);
                        try {
                            await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, message, this.cancel.Token);
                        } catch (Exception ex) {
                            this.DispatchIfCurrent(() => {
                                this.app.Window.AddSystemMessage("Error sending message.");
                                Console.WriteLine($"Error sending message: {ex.Message}");
                                Console.WriteLine(ex.StackTrace);
                            });
                            break;
                        }
                    } else if (result == outgoingMessage) {
                        var toSend = await outgoingMessage;
                        outgoingMessage = this.outgoingMessages.Reader.ReadAsync().AsTask();

                        try {
                            await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, toSend, this.cancel.Token);
                        } catch (Exception ex) {
                            this.DispatchIfCurrent(() => {
                                this.app.Window.AddSystemMessage("Error sending message.");
                                Console.WriteLine($"Error sending message: {ex.Message}");
                                Console.WriteLine(ex.StackTrace);
                            });
                            break;
                        }
                    } else if (result == cancel) {
                        break;
                    }
                }

                // remove player data
                this.SetPlayerData(null);

                // set availability
                this.Available = false;

                // at this point, we are disconnected, so log it
                this.DispatchIfCurrent(() => {
                    this.app.Window.AddSystemMessage("Disconnected");
                });

                // wait up to a second to send the shutdown packet
                await Task.WhenAny(Task.Delay(1_000), SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, ClientShutdown.Instance));

            Close:
                try {
                    this.client?.Close();
                } catch (ObjectDisposedException) {
                }
            } catch (Exception ex) {
                if (!this.cancel.IsCancellationRequested && !(ex is OperationCanceledException)) {
                    this.DispatchIfCurrent(() => {
                        this.app.Window.AddSystemMessage($"连接或通信中断: {ex.Message}");
                        if (ReferenceEquals(this.app.Connection, this)) this.app.Disconnect();
                    });
                }
            } finally {
                this.cancel.Cancel();
                this.client?.Dispose();
                if (receiver != null) await receiver;
                this.Available = false;
                this.DispatchIfCurrent(() => {
                    if (ReferenceEquals(this.app.Connection, this)) this.app.Disconnect();
                });
            }
        }

        private void DispatchIfCurrent(Action action) {
            this.app.Dispatch(() => {
                if (ReferenceEquals(this.app.Connection, this)) action();
            });
        }

        private Task HandleIncoming(byte[] rawMessage) {
            var type = (ServerOperation) rawMessage[0];
            var payload = new byte[rawMessage.Length - 1];
            Array.Copy(rawMessage, 1, payload, 0, payload.Length);

            switch (type) {
                case ServerOperation.Pong:
                    // no-op
                    break;
                case ServerOperation.Message:
                    var message = ServerMessage.Decode(payload);

                    this.DispatchIfCurrent(() => {
                        this.ReceiveMessage?.Invoke(message);
                        this.app.Window.AddMessage(message);
                    });
                    break;
                case ServerOperation.Shutdown:
                    this.DispatchIfCurrent(() => {
                        if (ReferenceEquals(this.app.Connection, this)) this.app.Disconnect();
                    });
                    break;
                case ServerOperation.PlayerData:
                    var playerData = payload.Length == 0 ? null : PlayerData.Decode(payload);

                    this.SetPlayerData(playerData);
                    break;
                case ServerOperation.Availability:
                    var availability = Availability.Decode(payload);

                    this.Available = availability.available;
                    break;
                case ServerOperation.Channel:
                    var channel = ServerChannel.Decode(payload);

                    this.CurrentChannel = channel.name;

                    this.DispatchIfCurrent(() => {
                        this.OnPropertyChanged(nameof(this.CurrentChannel));
                    });
                    break;
                case ServerOperation.Backlog:
                    var backlog = ServerBacklog.Decode(payload);

                    var seq = _backlogSequence;
                    foreach (var msg in backlog.messages.ToList().Chunks(100)) {
                        msg.Reverse();
                        var array = msg.ToArray();
                        this.DispatchIfCurrent(() => {
                            this.app.Window.AddReversedChunk(array, seq);
                        });
                    }

                    break;
                case ServerOperation.PlayerList:
                    break;
                case ServerOperation.LinkshellList:
                    break;
            }

            return Task.CompletedTask;
        }

        private static int _backlogSequence = -1;

        private void SetPlayerData(PlayerData? playerData) {
            var visibility = playerData == null ? Visibility.Collapsed : Visibility.Visible;

            this.DispatchIfCurrent(() => {
                if (!ReferenceEquals(this.app.Connection, this)) return;
                var window = this.app.Window;

                window.LoggedInAsText.Text = playerData?.name ?? "Not logged in";

                window.LoggedInAsSeparatorText.Visibility = visibility;

                window.CurrentWorldText.Text = playerData?.currentWorld;
                window.CurrentWorldText.Visibility = visibility;

                window.CurrentWorldSeparatorText.Visibility = visibility;

                window.LocationText.Text = playerData?.location;
                window.LocationButton.Visibility = visibility;
                window.CurrentPlayerData = playerData;
            });
        }

        private void OnPropertyChanged(string prop) {
            Action action;

            if (prop == nameof(this.Available)) {
                action = () => {
                    this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Available)));
                    this.app.Window.OnPropertyChanged(nameof(MainWindow.InputPlaceholder));
                };
            } else {
                action = () => {
                    this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
                };
            }

            this.DispatchIfCurrent(action);
        }
    }
}
