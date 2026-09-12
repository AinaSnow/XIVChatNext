using MessagePack;
using Sodium;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;

namespace XIVChatPlugin {
    internal class Server : IDisposable {
        private const int MaxMessageLength = 500;

        private static readonly string[] PublicPrefixes = [
            "/t ",
            "/tell ",
            "/reply ",
            "/r ",
            "/say ",
            "/s ",
            "/shout ",
            "/sh ",
            "/yell ",
            "/y ",
        ];

        private readonly Plugin _plugin;
        private readonly LinkMetadataCache _metadata;
        internal GameCardService GameCards { get; }
        private ScreenshotCapture ScreenshotCapture { get; }
        private ScreenshotCoordinator Screenshots { get; }
        internal (int Items, int Maps, long Hits, long Misses) CacheUsage => this._metadata.Usage;

        private readonly Stopwatch _sendWatch = new();

        private readonly CancellationTokenSource _tokenSource = new();
        private readonly GameCommandQueue _toGame = new();
        private GameCommandContext _gameContext = new(null, "", 0);
        private long _channelRevision;
        private ServerChannel _channelSnapshot = new(InputChannel.Say, "");

        private readonly ConcurrentDictionary<Guid, BaseClient> _clients = new();
        internal IReadOnlyDictionary<Guid, BaseClient> Clients => this._clients;
        internal readonly Channel<Tuple<BaseClient, Channel<bool>>> PendingClients = Channel.CreateBounded<Tuple<BaseClient, Channel<bool>>>(32);
        private readonly object _clientGate = new();

        internal FriendListCoordinator FriendLists { get; }
        private FriendPresenceCoordinator FriendPresence { get; }
        private string _ownerEpoch = Guid.NewGuid().ToString("N");
        private bool _loggedOut;

        private readonly MessageBacklog _backlog;
        private readonly string _runId = Guid.NewGuid().ToString("N");
        private long _eventSequence;
        private CharacterIdentity? _eventOwner;
        private DateTime? _loginEventAt;
        private (uint Id, DateTime At)? _territoryEvent;
        private readonly GameEventQueue _pendingDutyEvents = new();

        private TcpListener? _listener;

        private bool _sendPlayerData;
        private readonly ConcurrentDictionary<Guid, byte> _awaitingState = new();

        private volatile bool _running;
        internal bool Running => this._running;
        internal string? LastError { get; private set; }
        internal (int Count, long Bytes) BacklogUsage => this._backlog.Usage;
        internal (int Count, int Bytes) GameQueueUsage => this._toGame.Usage;
        private long _nextHousingCheck;

        private InputChannel _currentChannel = InputChannel.Say;
        private SeString? _currentChannelName;
        private string? _currentTellTarget;

        private ServerHousingLocation _lastHousingLocation;

        private const int MaxMessageSize = 128_000;

        internal Server(Plugin plugin) {
            this._plugin = plugin;
            this._metadata = new LinkMetadataCache(plugin.DataManager);
            this.ScreenshotCapture = new ScreenshotCapture(plugin);
            this.Screenshots = new ScreenshotCoordinator(this.ScreenshotCapture.CaptureAsync,
                ex => Plugin.Log.Warning(ex, "Could not capture game screenshot"));
            this.GameCards = new GameCardService(plugin, this._metadata, (id, reply) => {
                if (this._clients.TryGetValue(id, out var client)) client.Send(reply);
            }, reply => this.BroadcastMessage(reply, ClientPreference.GameCardsSupport));
            if (string.IsNullOrWhiteSpace(plugin.Config.ServiceId)) plugin.Config.ServiceId = Guid.NewGuid().ToString("N");
            plugin.Config.Save();
            if (this._plugin.Config.KeyPair == null) {
                this.RegenerateKeyPair();
            }

            this._lastHousingLocation = this._plugin.Functions.HousingLocation;
            this._backlog = new MessageBacklog(plugin.Config.ServiceId, this._runId);
            this.ApplyMessageSettings();
            this._channelSnapshot = new ServerChannel(this._currentChannel, this.LocalisedChannelName(this._currentChannel));
            this.RefreshGameContext();

            this._sendWatch.Start();
            this._eventOwner = this.CurrentIdentity();

            this.FriendLists = new FriendListCoordinator(new GameFriendListReader(plugin), (request, message) => {
                if (!request.Cancellation.IsCancellationRequested && this._clients.TryGetValue(request.ClientId, out var client))
                    client.Send(message);
            });
            this.FriendPresence = new FriendPresenceCoordinator(new GameFriendPresenceReader(plugin), (request, message) => {
                if (!request.Cancellation.IsCancellationRequested && this._clients.TryGetValue(request.ClientId, out var client)) client.Send(message);
            });
        }


        internal void Spawn() {
            var listener = new TcpListener(IPAddress.Any, this._plugin.Config.Port);
            this._listener = listener;
            try {
                listener.Start();
            } catch (SocketException ex) {
                this.LastError = ex.Message;
                Plugin.Log.Error($"Could not start XIVChat server: {ex.Message}");
                listener.Stop();
                return;
            }

            this._running = true;
            _ = Task.Run(async () => {
                try {
                    while (!this._tokenSource.IsCancellationRequested) {
                        var conn = await listener.AcceptTcpClientAsync(this._tokenSource.Token);
                        this.SpawnClientTask(new TcpConnected(conn), true);
                    }
                } catch (Exception) when (this._tokenSource.IsCancellationRequested) {
                    // Stopping the listener also interrupts a pending accept.
                } catch (Exception ex) {
                    this.LastError = ex.Message;
                    Plugin.Log.Error($"XIVChat listener stopped: {ex.Message}");
                } finally {
                    listener.Stop();
                    this._running = false;
                }
            });
        }

        internal void RegenerateKeyPair() {
            this._plugin.Config.KeyPair = PublicKeyBox.GenerateKeyPair();
            this._plugin.Config.Save();
        }

        internal void OnChat(Dalamud.Game.Chat.IHandleableChatMessage chatMsg) {
            if (chatMsg.IsHandled) {
                return;
            }

            var type = chatMsg.LogKind;
            var sender = chatMsg.Sender;
            var message = chatMsg.Message;

            var chatCode = new ChatCode((ushort) type);

            if (!this._plugin.Config.SendBattle && chatCode.IsBattle()) {
                return;
            }

            var recipients = this._clients.Values.Where(c => c.Ready && c.Subscription.Allows((ushort)type)).ToArray();
            if (!this._backlog.Enabled && recipients.Length == 0) return;

            var chunks = new List<Chunk>();
            if (this._metadata.RefreshScope()) this.Formats.Clear();

            var colour = this._plugin.Functions.GetChannelColour(chatCode) ?? chatCode.DefaultColour();

            if (sender.Payloads.Count > 0) {
                var format = this.FormatFor(chatCode.Type);
                if (format is { IsPresent: true }) {
                    chunks.Add(new TextChunk(format.Before) {
                        FallbackColour = colour,
                    });
                    chunks.AddRange(ToChunks(sender, colour));
                    chunks.Add(new TextChunk(format.After) {
                        FallbackColour = colour,
                    });
                }
            }

            chunks.AddRange(ToChunks(message, colour));

            var msg = new ServerMessage(
                DateTime.UtcNow,
                (ChatType) type,
                sender.Encode(),
                message.Encode(),
                chunks
            );

            msg.Owner = this.CurrentIdentity();
            if (chatCode.Type is ChatType.TellIncoming or ChatType.TellOutgoing) {
                var peer = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
                if (peer != null) msg.TellPeer = new CharacterIdentity {
                    Name = peer.PlayerName, HomeWorldId = (ushort)peer.World.RowId,
                    HomeWorld = peer.World.Value.Name.ExtractText(),
                };
            }
            var encoded = this._backlog.Record(msg);
            foreach (var client in recipients) client.SendEncoded(encoded);
        }

        internal void ApplyMessageSettings() => this._backlog.Configure(this._plugin.Config.BacklogEnabled,
            this._plugin.Config.BacklogCount, Math.Clamp(this._plugin.Config.BacklogMaxMiB, 1, 256) * 1024L * 1024);

        internal void OnFrameworkUpdate(IFramework framework) {
            if (this._tokenSource.IsCancellationRequested) return;
            if (!this._awaitingState.IsEmpty || this._toGame.Usage.Count > 0 || Environment.TickCount64 >= this._nextHousingCheck)
                this._plugin.Functions.RefreshChatChannel();
            this.RefreshGameContext();
            this.FriendLists.Tick(this.CurrentIdentity(), this._ownerEpoch, DateTime.UtcNow);
            this.FriendPresence.Tick(this.CurrentIdentity(), this._ownerEpoch, DateTime.UtcNow);
            var eventOwner = this.CurrentIdentity();
            this.Screenshots.SetContext(eventOwner?.Key, this._ownerEpoch);
            this.GameCards.Tick(eventOwner, this._ownerEpoch, this._clients.Values.Any(c => c.Ready && c.GetPreference(ClientPreference.GameCardsSupport, false)));
            var player = eventOwner == null ? null : this._plugin.ObjectTable.LocalPlayer;
            if (eventOwner != null) this._eventOwner = eventOwner;
            if (player != null && this._sendPlayerData) {
                this._sendPlayerData = !this.BroadcastPlayerData();
            }

            if (Environment.TickCount64 >= this._nextHousingCheck) {
                this._nextHousingCheck = Environment.TickCount64 + 2_000;
                var housingLocation = this._plugin.Functions.HousingLocation;
                if (!Equals(housingLocation, this._lastHousingLocation)) {
                    this.BroadcastMessage(housingLocation, ClientPreference.HousingLocationSupport);
                    this._lastHousingLocation = housingLocation;
                }
            }
            foreach (var id in this._awaitingState.Keys.Take(32)) {
                if (!this._awaitingState.TryRemove(id, out _) || !this.Clients.TryGetValue(id, out var client) || !client.Ready) continue;
                client.Send(new Availability(player != null));
                client.Send((Encodable?)this.GeneratePlayerData() ?? EmptyPlayerData.Instance);
                client.Send(Volatile.Read(ref this._channelSnapshot));
                if (client.GetPreference(ClientPreference.HousingLocationSupport, false)) client.Send(this._lastHousingLocation);
            }
            // State precedes events so a new desktop can validate the active login episode.
            if (player != null && eventOwner != null) {
                if (this._loginEventAt is { } loginAt) {
                    this._loginEventAt = null;
                    this.BroadcastGameEvent(this.CreateGameEvent(GameEventKind.Login, eventOwner, loginAt));
                }
                if (this._territoryEvent is { } changed) {
                    this._territoryEvent = null;
                    var territory = this._plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(changed.Id);
                    this.BroadcastGameEvent(this.CreateGameEvent(GameEventKind.TerritoryChanged, eventOwner, changed.At,
                        changed.Id, territory?.PlaceName.ValueNullable?.Name.ExtractText() ?? ""));
                }
            }
            foreach (var pending in this._pendingDutyEvents.Drain(eventOwner?.Key, this._ownerEpoch)) {
                var duty = this.CreateGameEvent(GameEventKind.DutyReady, eventOwner!, pending.At, pending.DataId, pending.Name);
                duty.ExpiresAt = duty.Timestamp.AddSeconds(45);
                this.BroadcastGameEvent(duty);
            }
            // Remove invalid work promptly, but bound per-frame processing and execute at most one command.
            for (var i = 0; i < 32; i++) {
                var command = this._toGame.Peek();
                if (command == null) return;
                var failure = GameCommandQueue.Validate(command, Volatile.Read(ref this._gameContext));
                if (failure != null) {
                    if (this._toGame.Take(command) != null) this.RejectCommand(command.ClientId, command.RequestId, failure.Value, command.PartIndex);
                    continue;
                }
                var time = command.Channel != null ? 250 :
                    PublicPrefixes.Any(prefix => command.Text!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
                    this._currentChannel is InputChannel.Tell or InputChannel.Say or InputChannel.Shout or InputChannel.Yell ? 1_000 : 250;
                if (this._sendWatch.ElapsedMilliseconds < time) return;
                // Cancellation can remove the head between Peek and Take; revalidate the actual dequeued work.
                command = this._toGame.Take(command);
                if (command == null) continue;
                failure = GameCommandQueue.Validate(command, Volatile.Read(ref this._gameContext));
                if (failure != null) { this.RejectCommand(command.ClientId, command.RequestId, failure.Value, command.PartIndex); continue; }
                this._sendWatch.Restart();
                if (command.Channel is { } channel) {
                    if (!this._plugin.Functions.ChangeChatChannel(channel)) this.RejectCommand(command.ClientId, command.RequestId, CommandFailure.Unavailable);
                } else {
                    if (command.TellTarget is { } target) {
                        var world = this._plugin.DataManager.GetExcelSheet<World>().GetRowOrDefault(target.HomeWorldId);
                        if (!target.IsValid || world?.Name.ExtractText() != target.HomeWorld) {
                            this.RejectCommand(command.ClientId, command.RequestId, CommandFailure.InvalidRequest, command.PartIndex);
                            continue;
                        }
                    }
                    if (!this._plugin.Functions.ProcessChatBox(command.Text!)) this.RejectCommand(command.ClientId, command.RequestId, CommandFailure.Unavailable, command.PartIndex);
                    else if (command.TellTarget != null && command.LastPart) this.ReportTellStage(command.ClientId, command.RequestId, CommandStage.Submitted);
                }
                return;
            }
        }

        private static readonly IReadOnlyList<byte> Magic = new byte[] {
            14, 20, 67,
        };

        internal void SpawnClientTask(BaseClient client, bool requiresMagic) {
            var id = Guid.NewGuid();
            client.OnFailure = reason => { this.LastError = reason; Plugin.Log.Warning("Client {Id} disconnected: {Reason}", id, reason); };
            lock (this._clientGate) {
                if (this._tokenSource.IsCancellationRequested || this._clients.Count >= 32) {
                    client.Disconnect("Connection limit reached or server stopped.");
                    return;
                }
                this._clients[id] = client;
            }

            _ = Task.Run(async () => {
                Task? listen = null;
                using var stopRegistration = this._tokenSource.Token.Register(() => client.Disconnect());
                try {
                    using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(client.TokenSource.Token);
                    handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    if (requiresMagic) {
                        var magic = new byte[Magic.Count];
                        await client.ReadExactlyAsync(magic, 0, magic.Length, handshakeTimeout.Token);
                        if (!magic.SequenceEqual(Magic)) return;
                    }

                    var handshake = await KeyExchange.ServerHandshake(this._plugin.Config.KeyPair!, client, handshakeTimeout.Token);
                    client.Handshake = handshake;
                    handshakeTimeout.CancelAfter(Timeout.InfiniteTimeSpan);

                    if (!this._plugin.Config.TrustedKeys.Values.Any(entry => entry.Item2.SequenceEqual(handshake.RemotePublicKey))) {
                        if (!this._plugin.Config.AcceptNewClients) return;
                        var accepted = Channel.CreateBounded<bool>(1);
                        using var trustTimeout = CancellationTokenSource.CreateLinkedTokenSource(client.TokenSource.Token);
                        trustTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                        await this.PendingClients.Writer.WriteAsync(Tuple.Create(client, accepted), trustTimeout.Token);
                        if (!await accepted.Reader.ReadAsync(trustTimeout.Token)) return;
                    }

                    client.Connected = true;
                    client.Ready = true;
                    this._awaitingState[id] = 0;

                    listen = Task.Run(async () => {
                        try {
                            while (!client.TokenSource.IsCancellationRequested) {
                                var msg = await SecretMessage.ReadSecretMessage(client, handshake.Keys.rx, client.TokenSource.Token);
                                await this.ProcessMessage(id, client, msg);
                            }
                        } finally {
                            // Wake the send loop on EOF, malformed packets or cancellation.
                            client.Disconnect();
                        }
                    });

                    this._plugin.Events.FireNewClientEvent(id, client);
                    while (!client.TokenSource.IsCancellationRequested) {
                        using var packet = await client.Queue.ReadAsync(client.TokenSource.Token);
                        using var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(client.TokenSource.Token);
                        writeTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                        try {
                            await SecretMessage.SendSecretMessage(client, handshake.Keys.tx, packet.Bytes, writeTimeout.Token);
                        } catch (OperationCanceledException) when (!client.TokenSource.IsCancellationRequested) {
                            client.Disconnect("Outgoing write timed out after 10 seconds.");
                        }
                    }
                } catch (Exception) when (client.TokenSource.IsCancellationRequested) {
                } catch (EndOfStreamException) {
                    Plugin.Log.Info($"Client disconnected during handshake: {id}");
                } catch (Exception ex) {
                    Plugin.Log.Error($"Client connection failed: {ex.Message}");
                } finally {
                    this.RemoveClient(id);
                    if (listen != null) {
                        try {
                            await listen;
                        } catch (OperationCanceledException) {
                        } catch (EndOfStreamException) {
                        } catch (Exception ex) {
                            Plugin.Log.Info($"Client receive loop ended: {ex.Message}");
                        }
                    }
                }
            });
        }

        internal void RemoveClient(Guid id) {
            if (!this._clients.TryRemove(id, out var client)) {
                return;
            }

            client.Disconnect();
            this._toGame.CancelClient(id);
            this._awaitingState.TryRemove(id, out _);
        }

        private async Task ProcessMessage(Guid id, BaseClient client, byte[] msg) {
            if (msg.Length == 0) throw new InvalidDataException("Empty client packet.");
            var op = (ClientOperation) msg[0];

            var payload = new byte[msg.Length - 1];
            Array.Copy(msg, 1, payload, 0, payload.Length);

            switch (op) {
                case ClientOperation.Ping:
                    client.Send(Pong.Instance);
                    break;
                case ClientOperation.Message:
                    var clientMessage = ClientMessage.Decode(payload);
                    if (clientMessage.Content == null || Encoding.UTF8.GetByteCount(clientMessage.Content) > 8 * 1024) {
                        this.RejectCommand(id, clientMessage.RequestId, CommandFailure.InvalidRequest); break;
                    }
                    if (clientMessage.TellTarget != null && (!clientMessage.TellTarget.IsValid ||
                        !client.GetPreference(ClientPreference.GuardedCommandsSupport, false))) {
                        this.RejectCommand(id, clientMessage.RequestId, CommandFailure.InvalidRequest); break;
                    }
                    var context = this.CommandContext(id, client, clientMessage.RequestId, clientMessage.ExpectedOwnerKey,
                        clientMessage.ExpectedOwnerEpoch, clientMessage.ExpectedChannelRevision, clientMessage.TellTarget == null);
                    if (context == null) break;
                    var sanitised = clientMessage.Content
                        .Replace("\r\n", " ")
                        .Replace('\r', ' ')
                        .Replace('\n', ' ');
                    if (string.IsNullOrWhiteSpace(sanitised)) break;
                    GameCommand[] commands;
                    try {
                        if (clientMessage.TellTarget is { } tell) sanitised = tell.Format(sanitised);
                        var parts = ChatTextSplitter.Split(sanitised);
                        commands = parts.Select((part, index) => new GameCommand(id, clientMessage.RequestId, context,
                            part, null, client.TokenSource.Token, clientMessage.TellTarget, index == parts.Length - 1, index)).ToArray();
                    } catch (ArgumentException) { this.RejectCommand(id, clientMessage.RequestId, CommandFailure.InvalidRequest); break; }
                    if (!this._toGame.TryEnqueue(commands)) this.RejectCommand(id, clientMessage.RequestId, CommandFailure.QueueFull);
                    else if (clientMessage.TellTarget != null) this.ReportTellStage(id, clientMessage.RequestId, CommandStage.Queued);

                    break;
                case ClientOperation.Shutdown:
                    client.Disconnect();
                    break;
                case ClientOperation.Backlog:
                    // ReSharper disable once LocalVariableHidesMember
                    var backlog = ClientBacklog.Decode(payload);

                    var backlogMessages = this._backlog.Snapshot().Messages.Reverse()
                        .Where(m => client.Subscription.Allows((ushort)m.Channel)).Take(backlog.Amount).ToList();

                    if (!client.GetPreference(ClientPreference.BacklogNewestMessagesFirst, false)) {
                        backlogMessages.Reverse();
                    }

                    await SendBacklogs(backlogMessages.ToArray(), client);
                    break;
                case ClientOperation.CatchUp:
                    var catchUp = ClientCatchUp.Decode(payload);
                    // I'm not sure why this needs to be done, but apparently it does
                    var after = catchUp.After.AddMilliseconds(1);
                    var msgs = this.MessagesAfter(after);

                    if (client.GetPreference(ClientPreference.BacklogNewestMessagesFirst, false)) {
                        msgs = msgs.Reverse();
                    }

                    await SendBacklogs(msgs, client);
                    break;
                case ClientOperation.FriendPresence:
                    if (!client.GetPreference(ClientPreference.WorkbenchSupport, false) || payload.Length > 1024) break;
                    var presenceRequest = ClientFriendPresence.Decode(payload);
                    if (!presenceRequest.Valid) break;
                    if (!this.FriendPresence.Enqueue(new PresenceRequest(id, presenceRequest, client.TokenSource.Token)))
                        client.Send(new ServerFriendPresence { RequestId = presenceRequest.RequestId, OwnerKey = presenceRequest.OwnerKey,
                            OwnerEpoch = presenceRequest.OwnerEpoch, ContentId = presenceRequest.ContentId, Status = FriendListStatus.Busy });
                    break;
                case ClientOperation.PlayerList:
                    var playerList = ClientPlayerList.Decode(payload);

                    if (playerList.Type == PlayerListType.Friend) {
                        if (playerList.RequestId?.Length > 64 || playerList.ExpectedOwnerKey?.Length > 128 || playerList.ExpectedOwnerEpoch?.Length > 64) break;
                        if (!this.FriendLists.Enqueue(new FriendRequest(id, playerList, client.TokenSource.Token)))
                            client.Send(FriendListProtocol.Error(playerList.RequestId, null, null, FriendListStatus.Busy));
                    }

                    break;
                case ClientOperation.Preferences:
                    var preferences = ClientPreferences.Decode(payload);
                    var hadWorkbench = client.GetPreference(ClientPreference.WorkbenchSupport, false);
                    client.Subscription = new ChannelSubscription(preferences.Channels);
                    client.Preferences = preferences;
                    if (!hadWorkbench && client.GetPreference(ClientPreference.WorkbenchSupport, false)) {
                        client.Send(new ServerCapabilities {
                            ServiceId = this._plugin.Config.ServiceId, RunId = this._runId, CursorBacklog = true, FriendSnapshots = true,
                            ChannelSubscriptions = true, GuardedCommands = true, DirectedTell = true, FriendPresence = true, GameEvents = true, GameCards = true, Screenshots = true,
                        });
                    }

                    // immediately queue housing location
                    if (client.GetPreference(ClientPreference.HousingLocationSupport, false)) {
                        this._awaitingState[id] = 0;
                    }

                    break;
                case ClientOperation.Screenshot:
                    if (!client.GetPreference(ClientPreference.ScreenshotSupport, false) || payload.Length > 1024) break;
                    var screenshot = ClientScreenshot.Decode(payload);
                    if (screenshot.Valid) _ = this.Screenshots.RequestAsync(id, screenshot, client.TokenSource.Token,
                        (packet, bulk) => bulk ? client.TrySendScreenshot(packet.Encode()) : client.Send(packet));
                    break;
                case ClientOperation.GameCard:
                    if (!client.GetPreference(ClientPreference.GameCardsSupport, false) || payload.Length > 2048) break;
                    var cardRequest = ClientGameCard.Decode(payload);
                    if (!cardRequest.Valid) break;
                    if (!this.GameCards.Enqueue(new GameCardRequest(id, cardRequest, client.TokenSource.Token, DateTime.UtcNow)))
                        client.Send(new ServerGameCard { RequestId = cardRequest.RequestId, Query = cardRequest.Query,
                            Id = cardRequest.Id, ItemKind = cardRequest.ItemKind, Status = CardStatus.Busy });
                    break;
                case ClientOperation.History:
                    if (client.GetPreference(ClientPreference.WorkbenchSupport, false)) {
                        var request = ClientHistory.Decode(payload);
                        var page = this.HistoryPage(request);
                        client.Send(page);
                    }
                    break;
                case ClientOperation.Channel:
                    var channel = ClientChannel.Decode(payload);
                    if (!Enum.IsDefined(channel.Channel)) { this.RejectCommand(id, channel.RequestId, CommandFailure.InvalidRequest); break; }
                    var channelContext = this.CommandContext(id, client, channel.RequestId, channel.ExpectedOwnerKey, channel.ExpectedOwnerEpoch, null, false);
                    if (channelContext != null && !this._toGame.TryEnqueue(new[] {
                        new GameCommand(id, channel.RequestId, channelContext, null, channel.Channel, client.TokenSource.Token),
                    })) this.RejectCommand(id, channel.RequestId, CommandFailure.QueueFull);

                    break;
            }
        }

        private GameCommandContext? CommandContext(Guid id, BaseClient client, string? request, string? owner,
            string? epoch, long? channelRevision, bool checkChannel) {
            var context = Volatile.Read(ref this._gameContext);
            var failure = GameCommandQueue.ValidateRequest(context, client.GetPreference(ClientPreference.GuardedCommandsSupport, false),
                request, owner, epoch, channelRevision, checkChannel);
            if (failure == null) return context;
            this.RejectCommand(id, request, failure.Value);
            return null;
        }

        private void RejectCommand(Guid id, string? request, CommandFailure failure, int submittedParts = 0) {
            if (!string.IsNullOrEmpty(request)) this._toGame.CancelRequest(id, request);
            if (!this._clients.TryGetValue(id, out var client) || !client.Ready) return;
            if (client.GetPreference(ClientPreference.GuardedCommandsSupport, false))
                client.Send(new ServerCommandResult { RequestId = request?.Length <= 64 ? request : null, Failure = failure, SubmittedParts = submittedParts });
            else client.Send(new ServerMessage(DateTime.UtcNow, 0, Array.Empty<byte>(), Array.Empty<byte>(),
                new List<Chunk> { new TextChunk($"XIVChat: command cancelled ({failure}).") }));
        }

        private void ReportTellStage(Guid id, string? request, CommandStage stage) {
            if (this._clients.TryGetValue(id, out var client) && client.Ready)
                client.Send(new ServerCommandResult { RequestId = request, Stage = stage });
        }

        private void RefreshGameContext() {
            var owner = this.CurrentIdentity()?.Key;
            var previous = Volatile.Read(ref this._gameContext);
            if (previous.OwnerKey == owner && previous.Epoch == this._ownerEpoch && previous.ChannelRevision == this._channelRevision) return;
            Volatile.Write(ref this._gameContext, new GameCommandContext(owner, this._ownerEpoch, this._channelRevision));
        }

        internal class NameFormatting {
            internal string Before { get; private set; } = string.Empty;
            internal string After { get; private set; } = string.Empty;
            internal bool IsPresent { get; private set; } = true;

            internal static NameFormatting Empty() {
                return new() {
                    IsPresent = false,
                };
            }

            internal static NameFormatting Of(string before, string after) {
                return new() {
                    Before = before,
                    After = after,
                };
            }
        }

        private Dictionary<ChatType, NameFormatting> Formats { get; } = new();

        private NameFormatting? FormatFor(ChatType type) {
            if (this.Formats.TryGetValue(type, out var cached)) {
                return cached;
            }

            var logKind = this._plugin.DataManager.GetExcelSheet<LogKind>().GetRowOrDefault((ushort) type);

            if (logKind == null) {
                return this.Formats[type] = NameFormatting.Empty();
            }

            var format = logKind.Value.Format.ToDalamudString();

            var firstStringParam = format.Payloads.FindIndex(payload => IsStringParam(payload, 1));
            var secondStringParam = format.Payloads.FindIndex(payload => IsStringParam(payload, 2));

            if (firstStringParam == -1 || secondStringParam <= firstStringParam) {
                return this.Formats[type] = NameFormatting.Empty();
            }

            var before = format.Payloads
                .GetRange(0, firstStringParam)
                .Where(payload => payload is ITextProvider)
                .Cast<ITextProvider>()
                .Select(text => text.Text);
            var after = format.Payloads
                .GetRange(firstStringParam + 1, secondStringParam - firstStringParam)
                .Where(payload => payload is ITextProvider)
                .Cast<ITextProvider>()
                .Select(text => text.Text);

            var nameFormatting = NameFormatting.Of(
                string.Join("", before),
                string.Join("", after)
            );

            this.Formats[type] = nameFormatting;

            return nameFormatting;

            static bool IsStringParam(Payload payload, byte num) {
                var data = payload.Encode();

                return data is [_, 0x29, _, _, _, ..] && data[4] == num + 1;
            }
        }

        private static Task SendBacklogs(IEnumerable<ServerMessage> messages, BaseClient client) {
            const int defaultSize = 5 + SecretMessage.NonceSize + SecretMessage.MacSize;
            var size = defaultSize;
            var responseMessages = new List<ServerMessage>();

            bool SendBacklog() {
                var resp = new ServerBacklog(responseMessages.ToArray(), ++client.BacklogSequence);
                return client.Send(resp);
            }

            foreach (var catchUpMessage in messages.Where(m => client.Subscription.Allows((ushort)m.Channel))) {
                // FIXME: this is very gross
                var len = MessagePackSerializer.Serialize(catchUpMessage).Length;
                // send message if it would've gone over length
                if (size + len >= MaxMessageSize) {
                    if (!SendBacklog()) return Task.CompletedTask;

                    size = defaultSize;
                    responseMessages.Clear();
                }

                size += len;
                responseMessages.Add(catchUpMessage);
            }

            if (responseMessages.Count > 0) {
                SendBacklog();
            }
            return Task.CompletedTask;
        }

        private IEnumerable<Chunk> ToChunks(SeString msg, uint? defaultColour) {
            var chunks = new List<Chunk>();

            var italic = false;
            var foreground = new Stack<uint>();
            var glow = new Stack<uint>();

            uint? currentMapId = null;
            float? currentMapX = null;
            float? currentMapY = null;
            string? currentMapFilenameId = null;
            ushort? currentMapSizeFactor = null;
            string? currentMapPlaceName = null;
            uint? currentItemId = null;
            uint? currentItemKind = null;
            bool? currentIsHq = null;
            string? currentItemName = null;
            string? currentItemDesc = null;
            uint? currentItemIcon = null;
            ushort? currentItemLevel = null;
            byte? currentItemRarity = null;
            string? currentItemCategory = null;
            ushort? currentItemEquipLevel = null;
            byte? currentItemMateriaSlots = null;
            bool? currentItemIsAdvancedMeldingPermitted = null;
            List<string>? currentItemStats = null;
            XIVChatCommon.GameItemDetails? currentItemDetails = null;
            XIVChatCommon.GameDataSource? currentDataSource = null;

            void Append(string text) {
                chunks.Add(new TextChunk(text) {
                    FallbackColour = defaultColour,
                    Foreground = foreground.Count > 0 ? foreground.Peek() : null,
                    Glow = glow.Count > 0 ? glow.Peek() : null,
                    Italic = italic,
                    MapId = currentMapId,
                    MapX = currentMapX,
                    MapY = currentMapY,
                    MapFilenameId = currentMapFilenameId,
                    MapSizeFactor = currentMapSizeFactor,
                    MapPlaceName = currentMapPlaceName,
                    ItemId = currentItemId,
                    ItemKind = currentItemKind,
                    IsHq = currentIsHq,
                    ItemName = currentItemName,
                    ItemDescription = currentItemDesc,
                    ItemIconId = currentItemIcon,
                    ItemLevel = currentItemLevel,
                    ItemRarity = currentItemRarity,
                    ItemCategory = currentItemCategory,
                    ItemEquipLevel = currentItemEquipLevel,
                    ItemMateriaSlots = currentItemMateriaSlots,
                    ItemIsAdvancedMeldingPermitted = currentItemIsAdvancedMeldingPermitted,
                    ItemStats = currentItemStats,
                    ItemDetails = currentItemDetails,
                    DataSource = currentDataSource,
                });
            }

            foreach (var payload in msg.Payloads) {
                switch (payload.Type) {
                    case PayloadType.EmphasisItalic:
                        var newStatus = ((EmphasisItalicPayload) payload).IsEnabled;
                        italic = newStatus;
                        break;
                    case PayloadType.UIForeground:
                        var foregroundPayload = (UIForegroundPayload) payload;
                        if (foregroundPayload.IsEnabled) {
                            foreground.Push(foregroundPayload.UIColor.Value.Dark);
                        } else if (foreground.Count > 0) {
                            foreground.Pop();
                        }

                        break;
                    case PayloadType.UIGlow:
                        var glowPayload = (UIGlowPayload) payload;
                        if (glowPayload.IsEnabled) {
                            glow.Push(glowPayload.UIColor.Value.Light);
                        } else if (glow.Count > 0) {
                            glow.Pop();
                        }

                        break;
                    case PayloadType.AutoTranslateText:
                        chunks.Add(new IconChunk {
                            index = 54,
                        });
                        var autoText = ((AutoTranslatePayload) payload).Text;
                        Append(autoText.Substring(2, autoText.Length - 4));
                        chunks.Add(new IconChunk {
                            index = 55,
                        });
                        break;
                    case PayloadType.Icon:
                        var index = ((IconPayload) payload).Icon;
                        chunks.Add(new IconChunk {
                            index = (byte) index,
                        });
                        break;
                    case PayloadType.MapLink:
                        var mapLink = (MapLinkPayload)payload;
                        var map = this._metadata.Map(mapLink.Map.RowId, mapLink.TerritoryType.RowId);
                        currentMapId = map.Id > 0 ? map.Id : null;
                        currentMapX = mapLink.XCoord; currentMapY = mapLink.YCoord;
                        currentMapFilenameId = map.Filename; currentMapSizeFactor = map.SizeFactor;
                        currentMapPlaceName = map.PlaceName;
                        currentDataSource = this._metadata.Source();
                        break;
                    case PayloadType.Item:
                        var itemLink = (ItemPayload)payload;
                        currentItemId = itemLink.ItemId; currentIsHq = itemLink.IsHQ;
                        currentItemKind = (uint)itemLink.Kind;
                        var item = this._metadata.Item(itemLink.ItemId, itemLink.Kind);
                        currentItemName = item?.Name; currentItemDesc = item?.Description;
                        currentItemIcon = item?.Icon; currentItemLevel = item?.Level;
                        currentItemRarity = item?.Rarity; currentItemCategory = item?.Category;
                        currentItemEquipLevel = item?.EquipLevel; currentItemMateriaSlots = item?.MateriaSlots;
                        currentItemIsAdvancedMeldingPermitted = item?.AdvancedMelding; currentItemStats = item?.Stats;
                        currentItemDetails = item?.Details; currentDataSource = item?.Source;
                        break;
                    case PayloadType.Unknown:
                        var rawPayload = (RawPayload) payload;
                        if (rawPayload.Data.Length >= 2 && rawPayload.Data[1] == 0x13) {
                            if (foreground.Count > 0) {
                                foreground.Pop();
                            }

                            if (glow.Count > 0) {
                                glow.Pop();
                            }
                        }
                        if (rawPayload.Data.Length >= 4 && rawPayload.Data[0] == 0x02 && rawPayload.Data[1] == 0x27 && rawPayload.Data[3] == 0xCF) {
                            currentMapId = null;
                            currentMapX = null;
                            currentMapY = null;
                            currentMapFilenameId = null;
                            currentMapSizeFactor = null;
                            currentMapPlaceName = null;
                            currentItemId = null;
                            currentItemKind = null;
                            currentIsHq = null;
                            currentItemName = null;
                            currentItemDesc = null;
                            currentItemIcon = null;
                            currentItemLevel = null;
                            currentItemRarity = null;
                            currentItemCategory = null;
                            currentItemEquipLevel = null;
                            currentItemMateriaSlots = null;
                            currentItemIsAdvancedMeldingPermitted = null;
                            currentItemStats = null;
                            currentItemDetails = null; currentDataSource = null;
                        }

                        break;
                    default:
                        if (payload is ITextProvider textProvider) {
                            Append(textProvider.Text);
                        }

                        break;
                }
            }

            return chunks;
        }

        private IEnumerable<ServerMessage> MessagesAfter(DateTime time) {
            return this._backlog.Snapshot().Messages.Where(msg => msg.Timestamp > time).ToArray();
        }

        private ServerHistory HistoryPage(ClientHistory request) {
            var snapshot = this._backlog.Snapshot();
            return HistoryPager.Create(snapshot.Messages, this._plugin.Config.ServiceId, this._runId, snapshot.Latest, request);
        }

        private void BroadcastMessage(Encodable message) {
            var encoded = message.Encode();
            foreach (var client in this.Clients.Values) {
                if (client.Ready) client.SendEncoded(encoded);
            }
        }

        private void BroadcastMessage(Encodable message, ClientPreference preference) {
            var encoded = message.Encode();
            foreach (var client in this.Clients.Values) {
                if (client.Ready && client.GetPreference(preference, false)) {
                    client.SendEncoded(encoded);
                }
            }
        }

        private string LocalisedChannelName(InputChannel channel) {
            uint rowId = channel switch {
                InputChannel.Tell => 3,
                InputChannel.Say => 1,
                InputChannel.Party => 4,
                InputChannel.Alliance => 17,
                InputChannel.Yell => 16,
                InputChannel.Shout => 2,
                InputChannel.FreeCompany => 7,
                InputChannel.PvpTeam => 19,
                InputChannel.NoviceNetwork => 18,
                InputChannel.CrossLinkshell1 => 20,
                InputChannel.CrossLinkshell2 => 300,
                InputChannel.CrossLinkshell3 => 301,
                InputChannel.CrossLinkshell4 => 302,
                InputChannel.CrossLinkshell5 => 303,
                InputChannel.CrossLinkshell6 => 304,
                InputChannel.CrossLinkshell7 => 305,
                InputChannel.CrossLinkshell8 => 306,
                InputChannel.Linkshell1 => 8,
                InputChannel.Linkshell2 => 9,
                InputChannel.Linkshell3 => 10,
                InputChannel.Linkshell4 => 11,
                InputChannel.Linkshell5 => 12,
                InputChannel.Linkshell6 => 13,
                InputChannel.Linkshell7 => 14,
                InputChannel.Linkshell8 => 15,
                _ => 0,
            };

            return this._plugin.DataManager.GetExcelSheet<LogFilter>().GetRowOrDefault(rowId)?.Name.ExtractText() ?? string.Empty;
        }

        internal void OnChatChannelChange(uint channel, SeString name, string? tellTarget = null) {
            // for now, to avoid changing the protocol further, convert crossworld icon into font icon
            for (var i = 0; i < name.Payloads.Count; i++) {
                var payload = name.Payloads[i];
                if (payload is IconPayload { Icon: BitmapFontIcon.CrossWorld }) {
                    name.Payloads[i] = new TextPayload("\ue05d");
                }
            }

            var inputChannel = (InputChannel) channel;
            if (inputChannel == this._currentChannel && tellTarget == this._currentTellTarget && name.Encode().SequenceEqual(this._currentChannelName?.Encode() ?? [])) {
                return;
            }

            this._currentChannel = inputChannel;
            this._currentChannelName = name;
            this._currentTellTarget = tellTarget;

            var msg = new ServerChannel(inputChannel, name.TextValue) { Revision = ++this._channelRevision };
            Volatile.Write(ref this._channelSnapshot, msg);
            this.RefreshGameContext();
            this.BroadcastMessage(msg);
        }

        private void BroadcastAvailability(bool available) {
            this.BroadcastMessage(new Availability(available));
        }

        private PlayerData? GeneratePlayerData() {
            var identity = this.CurrentIdentity();
            if (identity == null) return null;
            var player = this._plugin.ObjectTable.LocalPlayer;
            if (player == null) {
                return null;
            }

            var homeWorldRow = player.HomeWorld.ValueNullable;
            var currentWorldRow = player.CurrentWorld.ValueNullable;
            if (homeWorldRow == null || currentWorldRow == null) return null;
            var homeWorld = homeWorldRow.Value.Name.ExtractText();
            var currentWorld = currentWorldRow.Value.Name.ExtractText();
            var territoryType = this._plugin.ClientState.TerritoryType;
            var territory = this._plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryType);
            var location = territory?.PlaceName.ValueNullable?.Name.ExtractText() ?? "???";
            var name = player.Name.TextValue;

            var mapId = this._plugin.ClientState.MapId;
            if (mapId == 0) {
                mapId = territory?.Map.RowId ?? territoryType;
            }

            uint? mapIdOpt = mapId > 0 ? mapId : null;
            float? mapX = null;
            float? mapY = null;
            string? mapFilenameId = null;
            ushort? mapSizeFactor = null;
            if (mapId > 0) {
                var mapRow = this._plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>().GetRowOrDefault(mapId);
                if (mapRow.HasValue) {
                    mapX = Dalamud.Utility.MapUtil.ConvertWorldCoordXZToMapCoord(player.Position.X, mapRow.Value.SizeFactor, mapRow.Value.OffsetX);
                    mapY = Dalamud.Utility.MapUtil.ConvertWorldCoordXZToMapCoord(player.Position.Z, mapRow.Value.SizeFactor, mapRow.Value.OffsetY);
                    mapFilenameId = mapRow.Value.Id.ExtractText();
                    mapSizeFactor = mapRow.Value.SizeFactor;
                }
            }

            return new PlayerData(homeWorld, currentWorld, location, name, mapIdOpt, mapX, mapY, mapFilenameId, mapSizeFactor) {
                Identity = identity, OwnerEpoch = this._ownerEpoch,
            };
        }

        private CharacterIdentity? CurrentIdentity() {
            // Logout clears world row references before IsLoaded necessarily becomes false.
            if (this._loggedOut || !this._plugin.ClientState.IsLoggedIn) return null;
            var state = this._plugin.PlayerState;
            if (!state.IsLoaded) return null;
            var contentId = state.ContentId;
            var name = state.CharacterName;
            var homeWorld = state.HomeWorld;
            var world = homeWorld.ValueNullable;
            if (contentId == 0 || string.IsNullOrWhiteSpace(name) || homeWorld.RowId == 0 || world == null) return null;
            return new CharacterIdentity {
                ContentId = contentId, Name = name,
                HomeWorldId = (ushort)homeWorld.RowId, HomeWorld = world.Value.Name.ExtractText(),
            };
        }

        private bool BroadcastPlayerData() {
            var playerData = this.GeneratePlayerData();
            if (playerData == null) return false;
            this.BroadcastMessage(playerData);
            return true;
        }

        internal void OnLogIn() {
            this._loggedOut = false;
            this._ownerEpoch = Guid.NewGuid().ToString("N");
            this.Screenshots.SetContext(null, this._ownerEpoch);
            this._loginEventAt = DateTime.UtcNow;
            this.RefreshGameContext();
            this._nextHousingCheck = 0;
            this.BroadcastAvailability(true);
            // send player data on next framework update
            this._sendPlayerData = true;
        }

        internal void OnLogOut(int type, int code) {
            this._loggedOut = true;
            if (this._eventOwner is { } owner) this.BroadcastGameEvent(this.CreateGameEvent(GameEventKind.Logout, owner, DateTime.UtcNow));
            this._eventOwner = null;
            this._loginEventAt = null;
            this._territoryEvent = null;
            this._pendingDutyEvents.Clear();
            this._sendPlayerData = false;
            this._ownerEpoch = Guid.NewGuid().ToString("N");
            Volatile.Write(ref this._gameContext, new GameCommandContext(null, this._ownerEpoch, this._channelRevision));
            this.Screenshots.SetContext(null, this._ownerEpoch);
            foreach (var command in this._toGame.Clear()) this.RejectCommand(command.ClientId, command.RequestId, CommandFailure.NotLoggedIn, command.PartIndex);
            this._nextHousingCheck = 0;
            this.FriendLists.Tick(null, this._ownerEpoch, DateTime.UtcNow);
            this.FriendPresence.Tick(null, this._ownerEpoch, DateTime.UtcNow);
            this.BroadcastAvailability(false);
            this.BroadcastMessage(EmptyPlayerData.Instance);
        }

        internal void OnTerritoryChange(uint territory) {
            this._sendPlayerData = true; this._nextHousingCheck = 0;
            this._territoryEvent = (territory, DateTime.UtcNow);
        }

        internal void OnDutyReady(ContentFinderCondition duty) {
            this._pendingDutyEvents.TryEnqueue(duty.RowId, duty.Name.ExtractText(), DateTime.UtcNow, Volatile.Read(ref this._gameContext));
        }

        private ServerGameEvent CreateGameEvent(GameEventKind kind, CharacterIdentity owner, DateTime at, uint dataId = 0, string name = "") => new() {
            EventId = $"{this._plugin.Config.ServiceId}/{this._runId}/event/{++this._eventSequence}",
            ServiceId = this._plugin.Config.ServiceId, RunId = this._runId,
            Owner = ConversationIdentity.Copy(owner), OwnerEpoch = this._ownerEpoch,
            Kind = kind, Timestamp = at, DataId = dataId, Name = name.Length > 256 ? name[..256] : name,
        };

        private void BroadcastGameEvent(ServerGameEvent entry) => this.BroadcastMessage(entry, ClientPreference.GameEventsSupport);

        public void Dispose() {
            this._tokenSource.Cancel();
            this._pendingDutyEvents.Complete();
            this._listener?.Stop();
            this._running = false;
            foreach (var id in this._clients.Keys) {
                this.RemoveClient(id);
            }

            this.FriendLists.Dispose();
            this.FriendPresence.Dispose();
            this.GameCards.Dispose();
            this.Screenshots.Dispose();
            this.ScreenshotCapture.Dispose();
            this._toGame.Clear();
            this.PendingClients.Writer.TryComplete();
        }
    }
}
