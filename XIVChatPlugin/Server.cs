using MessagePack;
using Sodium;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        private readonly Stopwatch _sendWatch = new();

        private readonly CancellationTokenSource _tokenSource = new();
        private readonly ConcurrentQueue<string> _toGame = new();

        private readonly ConcurrentDictionary<Guid, BaseClient> _clients = new();
        internal IReadOnlyDictionary<Guid, BaseClient> Clients => this._clients;
        internal readonly Channel<Tuple<BaseClient, Channel<bool>>> PendingClients = Channel.CreateUnbounded<Tuple<BaseClient, Channel<bool>>>();

        private readonly HashSet<Guid> _waitingForFriendList = [];

        private readonly LinkedList<ServerMessage> _backlog = [];

        private TcpListener? _listener;

        private bool _sendPlayerData;
        private readonly ConcurrentQueue<Guid> _awaitingPlayerData = new();
        private readonly ConcurrentQueue<Guid> _awaitingAvailability = new();
        private readonly ConcurrentQueue<Guid> _awaitingHousingLocation = new();

        private volatile bool _running;
        private bool Running => this._running;

        private InputChannel _currentChannel = InputChannel.Say;
        private SeString? _currentChannelName;

        private ServerHousingLocation _lastHousingLocation;

        private const int MaxMessageSize = 128_000;

        internal Server(Plugin plugin) {
            this._plugin = plugin;
            if (this._plugin.Config.KeyPair == null) {
                this.RegenerateKeyPair();
            }

            this._lastHousingLocation = this._plugin.Functions.HousingLocation;

            this._sendWatch.Start();

            this._plugin.Functions.ReceiveFriendList += this.OnReceiveFriendList;
        }


        private async void OnReceiveFriendList(List<Player> friends) {
            var msg = new ServerPlayerList(PlayerListType.Friend, friends.ToArray());

            foreach (var id in this._waitingForFriendList) {
                if (!this.Clients.TryGetValue(id, out var client)) {
                    continue;
                }

                await client.Queue.Writer.WriteAsync(msg);
            }

            this._waitingForFriendList.Clear();
        }

        internal void Spawn() {
            var port = this._plugin.Config.Port;

            Task.Run(async () => {
                this._listener = new TcpListener(IPAddress.Any, port);
                this._listener.Start();

                this._running = true;
                Plugin.Log.Info("Running...");
                while (!this._tokenSource.IsCancellationRequested) {
                    var conn = await this._listener.GetTcpClient(this._tokenSource);
                    if (conn == null) {
                        continue;
                    }

                    var client = new TcpConnected(conn);
                    this.SpawnClientTask(client, true);
                }

                this._running = false;
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

            var chunks = new List<Chunk>();

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

            this._backlog.AddLast(msg);
            while (this._backlog.Count > this._plugin.Config.BacklogCount) {
                this._backlog.RemoveFirst();
            }

            foreach (var client in this._clients.Values) {
                client.Queue.Writer.TryWrite(msg);
            }
        }

        internal void OnFrameworkUpdate(IFramework framework) {
            var player = this._plugin.ObjectTable.LocalPlayer;
            if (player != null && this._sendPlayerData) {
                this.BroadcastPlayerData();
                this._sendPlayerData = false;
            }

            var housingLocation = this._plugin.Functions.HousingLocation;
            if (!Equals(housingLocation, this._lastHousingLocation)) {
                this.BroadcastMessage(housingLocation, ClientPreference.HousingLocationSupport);
                this._lastHousingLocation = housingLocation;
            }

            while (this._awaitingPlayerData.TryDequeue(out var id)) {
                if (!this.Clients.TryGetValue(id, out var client)) {
                    continue;
                }

                var playerData = (Encodable?) this.GeneratePlayerData() ?? EmptyPlayerData.Instance;
                client.Queue.Writer.TryWrite(playerData);
            }

            while (this._awaitingAvailability.TryDequeue(out var id)) {
                if (!this.Clients.TryGetValue(id, out var client) || client.Handshake == null) {
                    continue;
                }

                var available = player != null;
                client.Queue.Writer.TryWrite(new Availability(available));
            }

            while (this._awaitingHousingLocation.TryDequeue(out var id)) {
                if (!this.Clients.TryGetValue(id, out var client) || client.Handshake == null) {
                    continue;
                }

                client.Queue.Writer.TryWrite(this._lastHousingLocation);
            }

            int time;
            if (this._toGame.TryPeek(out var peek) && PublicPrefixes.Any(prefix => peek.StartsWith(prefix))) {
                time = 1_000;
            } else if (this._currentChannel is InputChannel.Tell or InputChannel.Say or InputChannel.Shout or InputChannel.Yell) {
                time = 1_000;
            } else {
                time = 250;
            }

            if (this._sendWatch.Elapsed < TimeSpan.FromMilliseconds(time)) {
                return;
            }

            if (!this._toGame.TryDequeue(out var message)) {
                return;
            }

            this._sendWatch.Restart();

            this._plugin.Functions.ProcessChatBox(message);
        }

        private static readonly IReadOnlyList<byte> Magic = new byte[] {
            14, 20, 67,
        };

        internal void SpawnClientTask(BaseClient client, bool requiresMagic) {
            var id = Guid.NewGuid();
            this._clients[id] = client;

            Task.Run(async () => {
                if (requiresMagic) {
                    // get ready for reading magic bytes
                    var magic = new byte[Magic.Count];
                    var read = 0;

                    // only listen for magic for five seconds
                    using var cts = new CancellationTokenSource();
                    cts.CancelAfter(TimeSpan.FromSeconds(5));

                    // read magic bytes
                    while (read < magic.Length) {
                        if (cts.IsCancellationRequested) {
                            return;
                        }

                        read += await client.ReadAsync(magic, read, magic.Length - read, cts.Token);
                    }

                    // ignore this connection if incorrect magic bytes
                    if (!magic.SequenceEqual(Magic)) {
                        return;
                    }
                }

                var handshake = await KeyExchange.ServerHandshake(this._plugin.Config.KeyPair!, client);
                client.Handshake = handshake;

                // if this public key isn't trusted, prompt first
                if (!this._plugin.Config.TrustedKeys.Values.Any(entry => entry.Item2.SequenceEqual(handshake.RemotePublicKey))) {
                    // if configured to not accept new clients, reject connection
                    if (!this._plugin.Config.AcceptNewClients) {
                        return;
                    }

                    var accepted = Channel.CreateBounded<bool>(1);

                    await this.PendingClients.Writer.WriteAsync(Tuple.Create(client, accepted), this._tokenSource.Token);
                    if (!await accepted.Reader.ReadAsync(this._tokenSource.Token)) {
                        return;
                    }
                }

                client.Connected = true;

                // queue sending availability for this client
                this._awaitingAvailability.Enqueue(id);

                // queue sending player data for this client
                this._awaitingPlayerData.Enqueue(id);

                // send current channel
                try {
                    var channel = this._currentChannel;
                    await SecretMessage.SendSecretMessage(
                        client,
                        handshake.Keys.tx,
                        new ServerChannel(
                            channel,
                            this._currentChannelName?.TextValue ?? this.LocalisedChannelName(channel)
                        ),
                        this._tokenSource.Token
                    );
                } catch (Exception ex) {
                    Plugin.Log.Error($"Could not send message: {ex.Message}");
                }

                var listen = Task.Run(async () => {
                    while (this._clients.TryGetValue(id, out var client) && client.Connected && !client.TokenSource.IsCancellationRequested) {
                        byte[] msg;
                        try {
                            msg = await SecretMessage.ReadSecretMessage(client, handshake.Keys.rx, client.TokenSource.Token);
                        } catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut) {
                            continue;
                        } catch (Exception ex) {
                            Plugin.Log.Error($"Could not read message: {ex.Message}");
                            continue;
                        }

                        await this.ProcessMessage(id, client, msg);
                    }
                });

                this._plugin.Events.FireNewClientEvent(id, client);

                while (this._clients.TryGetValue(id, out var client) && client.Connected && !client.TokenSource.IsCancellationRequested) {
                    try {
                        var msg = await client.Queue.Reader.ReadAsync(client.TokenSource.Token);
                        await SecretMessage.SendSecretMessage(client, handshake.Keys.tx, msg, client.TokenSource.Token);
                    } catch (Exception ex) {
                        Plugin.Log.Error($"Could not send message: {ex.Message}");
                    }
                }

                client.Disconnect();

                await listen;

                this._clients.TryRemove(id, out _);
                Plugin.Log.Info($"Client thread ended: {id}");
            }).ContinueWith(_ => {
                this.RemoveClient(id);
            });
        }

        internal void RemoveClient(Guid id) {
            if (!this._clients.TryRemove(id, out var client)) {
                return;
            }

            client.Disconnect();
        }

        private async Task ProcessMessage(Guid id, BaseClient client, byte[] msg) {
            var op = (ClientOperation) msg[0];

            var payload = new byte[msg.Length - 1];
            Array.Copy(msg, 1, payload, 0, payload.Length);

            switch (op) {
                case ClientOperation.Ping:
                    try {
                        await client.Queue.Writer.WriteAsync(Pong.Instance);
                    } catch (Exception ex) {
                        Plugin.Log.Error($"Could not send message: {ex.Message}");
                    }

                    break;
                case ClientOperation.Message:
                    var clientMessage = ClientMessage.Decode(payload);
                    var sanitised = clientMessage.Content
                        .Replace("\r\n", " ")
                        .Replace('\r', ' ')
                        .Replace('\n', ' ');
                    foreach (var part in Wrap(sanitised)) {
                        this._toGame.Enqueue(part);
                    }

                    break;
                case ClientOperation.Shutdown:
                    client.Disconnect();
                    break;
                case ClientOperation.Backlog:
                    // ReSharper disable once LocalVariableHidesMember
                    var backlog = ClientBacklog.Decode(payload);

                    var backlogMessages = new List<ServerMessage>();

                    var node = this._backlog.Last;
                    while (node != null) {
                        if (backlogMessages.Count >= backlog.Amount) {
                            break;
                        }

                        backlogMessages.Add(node.Value);
                        node = node.Previous;
                    }

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
                case ClientOperation.PlayerList:
                    var playerList = ClientPlayerList.Decode(payload);

                    if (playerList.Type == PlayerListType.Friend) {
                        this._waitingForFriendList.Add(id);

                        if (!this._plugin.Functions.RequestingFriendList && !this._plugin.Functions.RequestFriendList()) {
                            this._plugin.ChatGui.PrintError($"[{Plugin.Name}] Please open your friend list to enable friend list support. You should only need to do this on initial install or after updates.");
                        }
                    }

                    break;
                case ClientOperation.Preferences:
                    var preferences = ClientPreferences.Decode(payload);
                    client.Preferences = preferences;

                    // immediately queue housing location
                    if (client.GetPreference(ClientPreference.HousingLocationSupport, false)) {
                        this._awaitingHousingLocation.Enqueue(id);
                    }

                    break;
                case ClientOperation.Channel:
                    var channel = ClientChannel.Decode(payload);
                    this._plugin.Functions.ChangeChatChannel(channel.Channel);

                    break;
            }
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
                return null;
            }

            var format = logKind.Value.Format.ToDalamudString();

            var firstStringParam = format.Payloads.FindIndex(payload => IsStringParam(payload, 1));
            var secondStringParam = format.Payloads.FindIndex(payload => IsStringParam(payload, 2));

            if (firstStringParam == -1 || secondStringParam == -1) {
                return NameFormatting.Empty();
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

        private static async Task SendBacklogs(IEnumerable<ServerMessage> messages, BaseClient client) {
            const int defaultSize = 5 + SecretMessage.NonceSize + SecretMessage.MacSize;
            var size = defaultSize;
            var responseMessages = new List<ServerMessage>();

            async Task SendBacklog() {
                var resp = new ServerBacklog(responseMessages.ToArray(), ++client.BacklogSequence);
                try {
                    await client.Queue.Writer.WriteAsync(resp);
                } catch (Exception ex) {
                    Plugin.Log.Error($"Could not send backlog: {ex.Message}");
                }
            }

            foreach (var catchUpMessage in messages) {
                // FIXME: this is very gross
                var len = MessagePackSerializer.Serialize(catchUpMessage).Length;
                // send message if it would've gone over length
                if (size + len >= MaxMessageSize) {
                    await SendBacklog();

                    size = defaultSize;
                    responseMessages.Clear();
                }

                size += len;
                responseMessages.Add(catchUpMessage);
            }

            if (responseMessages.Count > 0) {
                await SendBacklog();
            }
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
                    ItemStats = currentItemStats
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
                        var mapLink = (Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload) payload;
                        uint mId = mapLink.Map.RowId;
                        if (mId == 0) {
                            var territoryRow = this._plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(mapLink.TerritoryType.RowId);
                            if (territoryRow.HasValue) {
                                mId = territoryRow.Value.Map.RowId;
                            }
                        }
                        currentMapId = mId > 0 ? mId : null;
                        currentMapX = mapLink.XCoord;
                        currentMapY = mapLink.YCoord;
                        if (currentMapId.HasValue && currentMapId.Value > 0) {
                            var mapRow = this._plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>().GetRowOrDefault(currentMapId.Value);
                            if (mapRow.HasValue) {
                                currentMapFilenameId = mapRow.Value.Id.ExtractText();
                                currentMapSizeFactor = mapRow.Value.SizeFactor;
                                var pName = mapRow.Value.PlaceName.ValueNullable?.Name.ExtractText();
                                if (!string.IsNullOrEmpty(pName)) currentMapPlaceName = pName;
                            }
                        }
                        break;
                    case PayloadType.Item:
                        var itemLink = (Dalamud.Game.Text.SeStringHandling.Payloads.ItemPayload) payload;
                        uint rawItemId = itemLink.ItemId;
                        bool rawIsHq = itemLink.IsHQ;
                        if (rawItemId > 1000000) {
                            rawIsHq = true;
                            rawItemId -= 1000000;
                        } else if (rawItemId > 500000) {
                            rawIsHq = true;
                            rawItemId -= 500000;
                        }
                        currentItemId = rawItemId;
                        currentIsHq = rawIsHq;
                        if (currentItemId.HasValue && currentItemId.Value > 0) {
                            try {
                                var itemRow = this._plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().GetRowOrDefault(currentItemId.Value);
                                if (itemRow.HasValue) {
                                    var row = itemRow.Value;
                                    currentItemName = row.Name.ExtractText();
                                    currentItemDesc = row.Description.ExtractText();
                                    currentItemIcon = row.Icon;
                                    currentItemLevel = (ushort)row.LevelItem.RowId;
                                    currentItemRarity = row.Rarity;
                                    currentItemCategory = row.ItemUICategory.ValueNullable?.Name.ExtractText() ?? "";
                                    currentItemEquipLevel = row.LevelEquip;
                                    currentItemMateriaSlots = row.MateriaSlotCount;
                                    currentItemIsAdvancedMeldingPermitted = row.IsAdvancedMeldingPermitted;

                                    var statsList = new List<string>();
                                    if (row.DamagePhys > 0) statsList.Add($"物理基本性能 {row.DamagePhys}");
                                    if (row.DamageMag > 0 && row.DamageMag != row.DamagePhys) statsList.Add($"魔法基本性能 {row.DamageMag}");
                                    if (row.DefensePhys > 0) statsList.Add($"物理防御力 {row.DefensePhys}");
                                    if (row.DefenseMag > 0 && row.DefenseMag != row.DefensePhys) statsList.Add($"魔法防御力 {row.DefenseMag}");

                                    if (row.BaseParam.Count > 0 && row.BaseParamValue.Count > 0) {
                                        for (int i = 0; i < row.BaseParam.Count && i < row.BaseParamValue.Count; i++) {
                                            var bp = row.BaseParam[i].ValueNullable;
                                            if (bp.HasValue && bp.Value.RowId > 0) {
                                                string sName = bp.Value.Name.ExtractText();
                                                int sVal = row.BaseParamValue[i];
                                                if (!string.IsNullOrEmpty(sName) && sVal > 0) {
                                                    statsList.Add($"{sName} +{sVal}");
                                                }
                                            }
                                        }
                                    }
                                    if (currentIsHq == true && row.BaseParamSpecial.Count > 0 && row.BaseParamValueSpecial.Count > 0) {
                                        for (int i = 0; i < row.BaseParamSpecial.Count && i < row.BaseParamValueSpecial.Count; i++) {
                                            var bpSpec = row.BaseParamSpecial[i].ValueNullable;
                                            if (bpSpec.HasValue && bpSpec.Value.RowId > 0) {
                                                string sName = bpSpec.Value.Name.ExtractText();
                                                int sVal = row.BaseParamValueSpecial[i];
                                                if (!string.IsNullOrEmpty(sName) && sVal > 0) {
                                                    statsList.Add($"{sName} +{sVal} (HQ)");
                                                }
                                            }
                                        }
                                    }
                                    currentItemStats = statsList;
                                }
                            } catch { }
                        }
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

        private IEnumerable<ServerMessage> MessagesAfter(DateTime time) => this._backlog.Where(msg => msg.Timestamp > time).ToArray();

        private static IEnumerable<string> Wrap(string input) {
            if (input.Length <= MaxMessageLength) {
                return new[] {
                    input,
                };
            }

            string prefix = string.Empty;
            if (input.StartsWith("/")) {
                var space = input.IndexOf(' ');
                if (space != -1) {
                    prefix = input[..space];
                    // handle wrapping tells
                    if (prefix is "/tell" or "/t") {
                        var tellSpace = input.IndexOfCount(' ', 3);
                        if (tellSpace != -1) {
                            prefix = input[..tellSpace];
                            input = input[(tellSpace + 1)..];
                        }
                    } else {
                        input = input[(space + 1)..];
                    }
                }
            }

            return NativeTools.Wrap(input, MaxMessageLength)
                .Select(text => $"{prefix} {text}")
                .ToArray();
        }

        private void BroadcastMessage(Encodable message) {
            foreach (var client in this.Clients.Values) {
                client.Queue.Writer.TryWrite(message);
            }
        }

        private void BroadcastMessage(Encodable message, ClientPreference preference) {
            foreach (var client in this.Clients.Values) {
                if (client.GetPreference(preference, false)) {
                    client.Queue.Writer.TryWrite(message);
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

        internal void OnChatChannelChange(uint channel, SeString name) {
            // for now, to avoid changing the protocol further, convert crossworld icon into font icon
            for (var i = 0; i < name.Payloads.Count; i++) {
                var payload = name.Payloads[i];
                if (payload is IconPayload { Icon: BitmapFontIcon.CrossWorld }) {
                    name.Payloads[i] = new TextPayload("\ue05d");
                }
            }

            var inputChannel = (InputChannel) channel;
            if (inputChannel == this._currentChannel && name.Encode().SequenceEqual(this._currentChannelName?.Encode() ?? [])) {
                return;
            }

            this._currentChannel = inputChannel;
            this._currentChannelName = name;

            var msg = new ServerChannel(inputChannel, name.TextValue);
            this.BroadcastMessage(msg);
        }

        private void BroadcastAvailability(bool available) {
            this.BroadcastMessage(new Availability(available));
        }

        private PlayerData? GeneratePlayerData() {
            var player = this._plugin.ObjectTable.LocalPlayer;
            if (player == null) {
                return null;
            }

            var homeWorld = player.HomeWorld.Value.Name.ExtractText();
            var currentWorld = player.CurrentWorld.Value.Name.ExtractText();
            var territoryType = this._plugin.ClientState.TerritoryType;
            var territory = this._plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryType);
            var location = territory?.PlaceName.Value.Name.ExtractText() ?? "???";
            var name = player.Name.TextValue;

            var mapId = this._plugin.ClientState.MapId;
            if (mapId == 0) {
                mapId = territory?.Map.Value.RowId ?? territoryType;
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

            return new PlayerData(homeWorld, currentWorld, location, name, mapIdOpt, mapX, mapY, mapFilenameId, mapSizeFactor);
        }

        private void BroadcastPlayerData() {
            var playerData = (Encodable?) this.GeneratePlayerData() ?? EmptyPlayerData.Instance;

            this.BroadcastMessage(playerData);
        }

        internal void OnLogIn() {
            this.BroadcastAvailability(true);
            // send player data on next framework update
            this._sendPlayerData = true;
        }

        internal void OnLogOut(int type, int code) {
            this.BroadcastAvailability(false);
            this.BroadcastPlayerData();
        }

        internal void OnTerritoryChange(uint territory) => this._sendPlayerData = true;

        public void Dispose() {
            // stop accepting new clients
            this._tokenSource.Cancel();
            foreach (var client in this._clients.Values) {
                Task.Run(async () => {
                    // tell clients we're shutting down
                    if (client.Handshake != null) {
                        try {
                            await SecretMessage.SendSecretMessage(client, client.Handshake.Keys.tx, ServerShutdown.Instance);
                        } catch (Exception) {
                            // ignored
                        }
                    }

                    // cancel threads for open clients
                    await client.TokenSource.CancelAsync();
                });
            }

            this._plugin.Functions.ReceiveFriendList -= this.OnReceiveFriendList;
        }
    }

    internal static class TcpListenerExt {
        internal static async Task<TcpClient?> GetTcpClient(this TcpListener listener, CancellationTokenSource source) {
            await using (source.Token.Register(listener.Stop)) {
                try {
                    var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    return client;
                } catch (ObjectDisposedException) {
                    // Token was canceled - swallow the exception and return null
                    if (source.Token.IsCancellationRequested) {
                        return null;
                    }

                    throw;
                }
            }
        }
    }
}
