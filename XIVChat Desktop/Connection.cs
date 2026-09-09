using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.Specialized;
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

        private readonly Channel<byte[]> outgoingMessages = Channel.CreateBounded<byte[]>(256);
        private readonly Channel<byte[]> incoming = Channel.CreateBounded<byte[]>(256);
        private string source = "";
        private ServerCapabilities? capabilities;
        private ClientPreferences preferences;
        private long? channelRevision;
        private PlayerData? commandPlayer;
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
            this.preferences = this.BuildPreferences();
            app.Config.Saved += this.UpdateSubscriptions;
            app.Config.PropertyChanged += this.ConfigChanged;
            app.Config.Tabs.CollectionChanged += this.CollectionsChanged;
            app.Config.Notifications.CollectionChanged += this.CollectionsChanged;
        }

        public bool SendMessage(string message) {
            if (!this.Available || this.cancel.IsCancellationRequested || string.IsNullOrWhiteSpace(message)) return false;
            if (System.Text.Encoding.UTF8.GetByteCount(message) > 8 * 1024) {
                this.ReportCommandFailure(CommandFailure.InvalidRequest); return false;
            }
            var player = this.commandPlayer;
            if (this.capabilities?.GuardedCommands == true && (player?.Identity?.Key == null || this.channelRevision == null)) {
                this.ReportCommandFailure(CommandFailure.NotLoggedIn); return false;
            }
            return this.QueuePacket(new ClientMessage(message) {
                RequestId = Guid.NewGuid().ToString("N"), ExpectedOwnerKey = player?.Identity?.Key,
                ExpectedOwnerEpoch = player?.OwnerEpoch, ExpectedChannelRevision = this.channelRevision,
            }.Encode());
        }

        public void ChangeChannel(InputChannel channel) {
            var msg = new ClientChannel {
                Channel = channel,
                RequestId = Guid.NewGuid().ToString("N"), ExpectedOwnerKey = this.commandPlayer?.Identity?.Key,
                ExpectedOwnerEpoch = this.commandPlayer?.OwnerEpoch,
            };
            if (this.Available) this.QueuePacket(msg.Encode());
        }

        private bool QueuePacket(byte[] packet) {
            if (this.cancel.IsCancellationRequested) return false;
            if (packet.Length + SecretMessage.MacSize <= 128_000 && this.outgoingMessages.Writer.TryWrite(packet)) return true;
            this.ReportCommandFailure(CommandFailure.QueueFull);
            this.Disconnect();
            return false;
        }

        private ClientPreferences BuildPreferences() => new() {
            Preferences = new Dictionary<ClientPreference, object> {
                { ClientPreference.BacklogNewestMessagesFirst, true },
                { ClientPreference.WorkbenchSupport, true },
                { ClientPreference.GuardedCommandsSupport, true },
            },
            Channels = ChannelSubscription.Union(this.app.Config.HistoryEnabled,
                this.app.Config.Tabs.SelectMany(t => t.Filter.Types).Distinct().SelectMany(t => t.Types()).Select(t => (ushort)t),
                this.app.Config.Notifications.SelectMany(n => n.Channels).Select(t => (ushort)t)),
        };

        public void UpdateSubscriptions() {
            var updated = this.BuildPreferences();
            if ((updated.Channels == null && this.preferences.Channels == null) ||
                (updated.Channels != null && this.preferences.Channels != null && updated.Channels.SequenceEqual(this.preferences.Channels))) return;
            this.preferences = updated;
            if (this.capabilities?.ChannelSubscriptions == true) this.QueuePacket(updated.Encode());
        }
        private void ConfigChanged(object? sender, PropertyChangedEventArgs args) {
            if (args.PropertyName == nameof(Configuration.HistoryEnabled)) this.UpdateSubscriptions();
        }
        private void CollectionsChanged(object? sender, NotifyCollectionChangedEventArgs args) => this.UpdateSubscriptions();
        private void ReportCommandFailure(CommandFailure failure) => this.DispatchIfCurrent(() =>
            this.app.Window.AddSystemMessage(LocalizationHelper.GetString("Command." + failure)));

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
                this.source = Convert.ToHexString(handshake.RemotePublicKey);

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
                var sameHost = this.app.LastHost == currentHost && this.app.Session.Source == this.source;
                if (!sameHost) {
                    this.DispatchIfCurrent(() => {
                        this.app.Session.Clear();
                        this.app.LastHost = currentHost;
                    });
                }
                this.DispatchIfCurrent(() => this.app.Session.Source = this.source);

                this.DispatchIfCurrent(() => {
                    this.app.Window.AddSystemMessage("Connected");
                });

                // tell the server our preferences
                await SecretMessage.SendSecretMessage(stream, handshake.Keys.tx, this.preferences, this.cancel.Token);

                // check if backlog or catch-up is needed
                if (sameHost) {
                    // catch-up
                    var lastRealMessage = this.app.Session.Messages.LastOrDefault(msg => msg.Channel != 0);
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
                var outgoingMessage = this.outgoingMessages.Reader.ReadAsync().AsTask();
                var cancel = this.cancelChannel.Reader.ReadAsync().AsTask();

                // listen for incoming and outgoing messages and cancel requests
                while (!this.cancel.IsCancellationRequested) {
                    var result = await Task.WhenAny(incoming, outgoingMessage, cancel);
                    if (result == incoming) {
                        var rawMessage = await incoming;
                        incoming = this.incoming.Reader.ReadAsync().AsTask();

                        await this.HandleIncoming(rawMessage);
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
                this.app.Config.Saved -= this.UpdateSubscriptions;
                this.app.Config.PropertyChanged -= this.ConfigChanged;
                this.app.Config.Tabs.CollectionChanged -= this.CollectionsChanged;
                this.app.Config.Notifications.CollectionChanged -= this.CollectionsChanged;
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

        private async Task HandleIncoming(byte[] rawMessage) {
            var type = (ServerOperation) rawMessage[0];
            var payload = new byte[rawMessage.Length - 1];
            Array.Copy(rawMessage, 1, payload, 0, payload.Length);

            switch (type) {
                case ServerOperation.Pong:
                    // no-op
                    break;
                case ServerOperation.Message:
                    var message = ServerMessage.Decode(payload);
                    await this.Record(message);

                    this.DispatchIfCurrent(() => {
                        if (this.app.Session.Add(message)) this.ReceiveMessage?.Invoke(message);
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
                    if (!availability.available) this.commandPlayer = null;
                    if (!availability.available) this.DispatchIfCurrent(() =>
                        this.app.Session.Friends.SetContext(this.source, null, null, this.capabilities?.FriendSnapshots == true));
                    break;
                case ServerOperation.Channel:
                    var channel = ServerChannel.Decode(payload);

                    this.CurrentChannel = channel.name;
                    this.channelRevision = channel.Revision;

                    this.DispatchIfCurrent(() => {
                        this.OnPropertyChanged(nameof(this.CurrentChannel));
                    });
                    break;
                case ServerOperation.Backlog:
                    var backlog = ServerBacklog.Decode(payload);
                    // New peers use cursor history; the eager legacy request remains safe for old peers.
                    if (this.capabilities?.CursorBacklog == true) break;
                    await Task.WhenAll(backlog.messages.Select(this.Record));

                    var seq = _backlogSequence;
                    foreach (var msg in backlog.messages.ToList().Chunks(100)) {
                        msg.Reverse();
                        var array = msg.ToArray();
                        this.DispatchIfCurrent(() => {
                            this.app.Session.AddBacklog(array, seq);
                        });
                    }

                    break;
                case ServerOperation.Capabilities:
                    var previousCapabilities = this.capabilities;
                    this.capabilities = ServerCapabilities.Decode(payload);
                    var newServerSession = previousCapabilities == null || previousCapabilities.ServiceId != this.capabilities.ServiceId ||
                        previousCapabilities.RunId != this.capabilities.RunId;
                    // Capture configuration changes made while capability negotiation was in flight.
                    if (newServerSession && this.capabilities.ChannelSubscriptions) this.QueuePacket(this.preferences.Encode());
                    this.DispatchIfCurrent(this.UpdateFriendsContext);
                    if (newServerSession && this.capabilities.CursorBacklog) {
                        HistoryCursor? cursor = null;
                        try {
                            if (this.app.Session.Store != null && this.app.Config.HistoryEnabled && this.app.Session.StorageError == null)
                                cursor = await this.app.Session.Store.GetCursorAsync(this.source, this.capabilities.ServiceId);
                        } catch (Exception ex) { this.app.Session.ReportStorageError(ex); }
                        this.QueuePacket(new ClientHistory { After = cursor }.Encode());
                    }
                    break;
                case ServerOperation.History:
                    if (this.capabilities?.CursorBacklog != true) break;
                    var history = ServerHistory.Decode(payload);
                    if (history.Cursor.ServiceId != this.capabilities.ServiceId || history.Cursor.RunId != this.capabilities.RunId) break;
                    await Task.WhenAll(history.Messages.Select(this.Record));
                    this.DispatchIfCurrent(() => {
                        if (history.HasGap) this.app.Window?.AddSystemMessage(LocalizationHelper.GetString("History.Gap"));
                        this.app.Session.AddCursorPage(history.Messages);
                    });
                    // Save progress only after every page record has committed. Live messages never advance this checkpoint.
                    if (this.app.Config.HistoryEnabled && this.app.Session.Store != null && this.app.Session.StorageError == null) {
                        try { await this.app.Session.Store.SaveCursorAsync(this.source, history.Cursor); }
                        catch (Exception ex) { this.app.Session.ReportStorageError(ex); }
                    }
                    if (history.HasMore) this.QueuePacket(new ClientHistory { After = history.Cursor, Through = history.Through }.Encode());
                    break;
                case ServerOperation.PlayerList:
                    if (this.capabilities?.FriendSnapshots != true || rawMessage.Length > FriendListProtocol.MaxPageBytes) break;
                    var friends = ServerPlayerList.Decode(payload);
                    this.DispatchIfCurrent(() => {
                        var state = this.app.Session.Friends;
                        var request = state.RequestId;
                        var snapshot = state.Add(friends);
                        if (snapshot != null) _ = this.SaveFriendsAsync(snapshot);
                        if (request != null && state.RequestId == null && state.Manual) this.ReportFriendResult();
                    });
                    break;
                case ServerOperation.LinkshellList:
                    break;
                case ServerOperation.CommandResult:
                    if (this.capabilities?.GuardedCommands == true)
                        this.ReportCommandFailure(ServerCommandResult.Decode(payload).Failure);
                    break;
            }

        }

        private async Task Record(ServerMessage message) {
            // Capture connection ownership before asynchronous persistence; a replaced connection must not pollute the next source.
            if (!ReferenceEquals(this.app.Connection, this)) return;
            await this.app.Session.RecordAsync(message, this.source);
        }

        private static int _backlogSequence = -1;

        private void UpdateFriendsContext() {
            var player = this.app.Session.Player;
            var state = this.app.Session.Friends;
            if (!state.SetContext(this.source, player?.Identity?.Key, player?.OwnerEpoch, this.capabilities?.FriendSnapshots == true)) return;
            if (state.OwnerKey != null) _ = this.RestoreFriendsAsync(state.Version, state.OwnerKey);
            this.RefreshFriends(false);
        }

        private async Task RestoreFriendsAsync(int version, string owner) {
            if (!this.app.Config.HistoryEnabled || this.app.Session.Store == null || this.app.Session.StorageError != null) return;
            try {
                var saved = await this.app.Session.Store.GetFriendSnapshotAsync(this.source, owner);
                this.DispatchIfCurrent(() => this.app.Session.Friends.Restore(version, saved));
            } catch (Exception ex) { this.app.Session.ReportStorageError(ex); }
        }

        private async Task SaveFriendsAsync(ServerPlayerList snapshot) {
            if (!this.app.Config.HistoryEnabled || this.app.Session.Store == null || this.app.Session.StorageError != null) return;
            try { await this.app.Session.Store.SaveFriendSnapshotAsync(this.source, snapshot); }
            catch (Exception ex) { this.app.Session.ReportStorageError(ex); }
        }

        public bool RefreshFriends(bool manual = true) {
            if (!ReferenceEquals(this.app.Connection, this) || this.cancel.IsCancellationRequested) return false;
            var request = this.app.Session.Friends.Begin(manual);
            if (request == null) return false;
            this.QueuePacket(request.Encode());
            _ = this.FriendTimeoutAsync(request.RequestId!);
            return true;
        }

        private async Task FriendTimeoutAsync(string requestId) {
            try {
                await Task.Delay(TimeSpan.FromSeconds(15), this.cancel.Token);
                this.DispatchIfCurrent(() => {
                    if (this.app.Session.Friends.Fail(requestId, FriendListStatus.TimedOut) && this.app.Session.Friends.Manual)
                        this.ReportFriendResult();
                });
            } catch (OperationCanceledException) { }
        }

        private void ReportFriendResult() {
            var state = this.app.Session.Friends;
            this.app.Window.AddSystemMessage(state.Status == FriendListStatus.Success
                ? string.Format(LocalizationHelper.GetString("FriendList.Updated"), state.Snapshot!.Players.Length)
                : LocalizationHelper.GetString("FriendList.Failed") + " (" + state.Status + ")");
        }

        private void SetPlayerData(PlayerData? playerData) {
            this.commandPlayer = playerData;
            var visibility = playerData == null ? Visibility.Collapsed : Visibility.Visible;

            this.DispatchIfCurrent(() => {
                if (!ReferenceEquals(this.app.Connection, this)) return;
                var previousOwner = this.app.Session.Player?.Identity?.Key;
                this.app.Session.SetPlayer(playerData);
                this.UpdateFriendsContext();
                if (playerData?.Identity?.Key is { } owner && owner != previousOwner) _ = this.app.RestorePlayerHistoryAsync(playerData);
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
