using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace XIVChatPlugin {
    internal class PluginUi {
        private Plugin Plugin { get; }

        private bool _showSettings;

        private bool ShowSettings {
            get => this._showSettings;
            set => this._showSettings = value;
        }

        private readonly Dictionary<Guid, Tuple<BaseClient, Channel<bool>>> _pending = new();
        private readonly Dictionary<Guid, string> _pendingNames = new(0);
        private string relayUrlDraft = "";
        private string relayCredentialDraft = "";
        private string? relayUiError;
        private string? relayInvitation;
        private Task<string>? relayInvitationTask;

        internal PluginUi(Plugin plugin) {
            this.Plugin = plugin ?? throw new ArgumentNullException(nameof(plugin), "Plugin cannot be null");
            this.portDraft = plugin.Config.Port;
            this.relayUrlDraft = plugin.Config.RelayUrl;
        }

        private static class Colours {
            internal static readonly Vector4 Primary = new(2 / 255f, 204 / 255f, 238 / 255f, 1.0f);
            internal static readonly Vector4 PrimaryDark = new(2 / 255f, 180 / 255f, 211 / 255f, 1.0f);
            internal static readonly Vector4 Background = new(46 / 255f, 46 / 255f, 46 / 255f, 1.0f);
            internal static readonly Vector4 Text = new(190 / 255f, 190 / 255f, 190 / 255f, 1.0f);
            internal static readonly Vector4 Button = new(90 / 255f, 89 / 255f, 90 / 255f, 1.0f);
            internal static readonly Vector4 ButtonActive = new(123 / 255f, 122 / 255f, 124 / 255f, 1.0f);
            internal static readonly Vector4 ButtonHovered = new(108 / 255f, 107 / 255f, 109 / 255f, 1.0f);

            internal static readonly Vector4 White = new(1f, 1f, 1f, 1f);
        }

        internal void Draw() {
            ImGui.PushStyleColor(ImGuiCol.TitleBg, Colours.PrimaryDark);
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Colours.Primary);
            ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Colours.PrimaryDark);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Colours.Background);
            ImGui.PushStyleColor(ImGuiCol.Text, Colours.Text);
            ImGui.PushStyleColor(ImGuiCol.Button, Colours.Button);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Colours.ButtonActive);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Colours.ButtonHovered);

            try {
                this.DrawInner();
            } finally {
                ImGui.PopStyleColor(8);
            }
        }

        private static T WithWhiteText<T>(Func<T> func) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colours.White);
            var ret = func();
            ImGui.PopStyleColor();
            return ret;
        }

        private static void WithWhiteText(Action func) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colours.White);
            func();
            ImGui.PopStyleColor();
        }

        private static bool Begin(string name, ImGuiWindowFlags flags) {
            return WithWhiteText(() => ImGui.Begin(name, flags));
        }

        private static bool Begin(string name, ref bool showSettings, ImGuiWindowFlags flags) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colours.White);
            var result = ImGui.Begin(name, ref showSettings, flags);
            ImGui.PopStyleColor();
            return result;
        }

        private static void TextWhite(string text) => WithWhiteText(() => ImGui.TextUnformatted(text));

        private static void HelpMarker(string text) {
            ImGui.TextDisabled("(?)");
            if (!ImGui.IsItemHovered()) {
                return;
            }

            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20f);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        private int portDraft;
        private string? portError;
        private bool Chinese => this.Plugin.Config.UiLanguage == 1 ||
            (this.Plugin.Config.UiLanguage == 0 && System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        private string T(string chinese, string english) => this.Chinese ? chinese : english;

        private void DrawInner() {
            this.AcceptPending();
            foreach (var item in this._pending.ToList()) {
                if (item.Value.Item1.TokenSource.IsCancellationRequested || this.DrawPending(item.Key, item.Value.Item1, item.Value.Item2)) {
                    this._pending.Remove(item.Key); this._pendingNames.Remove(item.Key);
                }
            }
            if (!this.ShowSettings) return;
            ImGui.SetNextWindowSize(new Vector2(640, 480), ImGuiCond.FirstUseEver);
            if (!Begin(Plugin.Name, ref this._showSettings, ImGuiWindowFlags.None)) { ImGui.End(); return; }
            var language = this.Plugin.Config.UiLanguage;
            ImGui.SetNextItemWidth(160);
            if (ImGui.Combo(this.T("界面语言", "Language") + "##language", ref language, "Auto\0简体中文\0English\0")) {
                this.Plugin.Config.UiLanguage = language; this.Plugin.Config.Save();
            }
            if (ImGui.BeginTabBar("settings-tabs")) {
                if (ImGui.BeginTabItem(this.T("连接", "Connection") + "###connection")) {
                    this.DrawConnection(); ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(this.T("消息", "Messages") + "###messages")) {
                    this.DrawMessages(); ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(this.T("设备", "Devices") + "###devices")) {
                    this.DrawDevices(); ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(this.T("诊断", "Diagnostics") + "###diagnostics")) {
                    this.DrawDiagnostics(); ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            ImGui.End();
        }

        private void DrawConnection() {
            ImGui.SetNextItemWidth(160);
            ImGui.InputInt(this.T("端口", "Port") + "##port", ref this.portDraft);
            ImGui.SameLine();
            var validPort = this.portDraft is >= 1 and <= 65535;
            ImGui.BeginDisabled(!validPort || this.portDraft == this.Plugin.Config.Port);
            if (ImGui.Button(this.T("应用", "Apply") + "##apply-port")) {
                this.portError = this.Plugin.ApplyPort((ushort)this.portDraft);
            }
            ImGui.EndDisabled();
            ImGui.TextWrapped(this.T("点击应用会重启监听并断开当前连接。请同时更新客户端连接端口。", "Apply restarts the listener and disconnects current clients. Update the client connection port too."));
            if (!validPort) ImGui.TextUnformatted(this.T("端口范围为 1–65535。", "Port must be between 1 and 65535."));
            if (this.portError != null) ImGui.TextWrapped(this.T("应用失败，已恢复原端口：", "Apply failed; the previous port was restored: ") + this.portError);
            ImGui.Separator();
            var acceptNew = this.Plugin.Config.AcceptNewClients;
            if (ImGui.Checkbox(this.T("允许新设备请求连接", "Allow new device requests") + "##accept-new", ref acceptNew)) {
                this.Plugin.Config.AcceptNewClients = acceptNew; this.Plugin.Config.Save();
            }
            ImGui.TextWrapped(this.T("新设备仍需要核对公钥并确认信任。关闭后仅允许已信任设备。", "New devices still require matching public keys and trust approval. When disabled, only trusted devices can connect."));
            if (ImGui.CollapsingHeader(this.T("中继连接", "Relay") + "###relay")) {
                var allowRelay = this.Plugin.Config.AllowRelayConnections;
                if (ImGui.Checkbox(this.T("启用中继", "Enable relay") + "##allow-relay", ref allowRelay)) {
                    var old = this.Plugin.Config.AllowRelayConnections;
                    try {
                        this.Plugin.Config.AllowRelayConnections = allowRelay; this.Plugin.Config.Save();
                        if (allowRelay) this.Plugin.StartRelay(); else this.Plugin.StopRelay();
                    } catch (Exception ex) { this.Plugin.Config.AllowRelayConnections = old; relayUiError = ex.Message; }
                }
                ImGui.TextWrapped(this.T("连接自己部署的中继服务。游戏电脑无需端口映射；服务端需要双方都能访问。", "Connect to your self-hosted relay. The game PC needs no port forwarding; both devices must be able to reach the relay."));
                ImGui.InputText(this.T("中继地址", "Relay address") + "##relay-url", ref relayUrlDraft, 512);
                ImGui.InputText(this.T("注册凭据（留空保留）", "Registration credential (blank keeps saved)") + "##relay-auth", ref relayCredentialDraft, 128, ImGuiInputTextFlags.Password);
                if (ImGui.Button(this.T("保存并重新连接中继", "Save and reconnect relay") + "##restart-relay")) {
                    try {
                        XIVChat.Relay.Protocol.RelayProtocol.BaseUri(relayUrlDraft.Trim());
                        var saved = string.IsNullOrWhiteSpace(relayCredentialDraft) ? this.Plugin.Config.RelayCredential : XIVChat.Relay.Transport.WindowsSecret.Protect(relayCredentialDraft.Trim());
                        if (saved == null) throw new InvalidOperationException(this.T("请填写自建服务签发的注册凭据。", "Enter the registration credential issued by your relay."));
                        var oldUrl = this.Plugin.Config.RelayUrl; var oldCredential = this.Plugin.Config.RelayCredential; var oldAuth = this.Plugin.Config.RelayAuth;
                        this.Plugin.Config.RelayUrl = relayUrlDraft.Trim(); this.Plugin.Config.RelayCredential = saved; this.Plugin.Config.RelayAuth = null;
                        try { this.Plugin.Config.Save(); }
                        catch { this.Plugin.Config.RelayUrl = oldUrl; this.Plugin.Config.RelayCredential = oldCredential; this.Plugin.Config.RelayAuth = oldAuth; throw; }
                        relayCredentialDraft = ""; relayInvitation = null; relayUiError = null;
                        this.Plugin.StopRelay(); if (this.Plugin.Config.AllowRelayConnections) this.Plugin.StartRelay();
                    } catch (Exception ex) { relayUiError = ex.Message; }
                }
                var status = this.Plugin.Relay?.Status ?? ConnectionStatus.Disconnected;
                ImGui.TextUnformatted(this.T("中继状态：", "Relay status: ") + this.RelayStatus(status));
                if (Relay.ConnectionError != null) ImGui.TextWrapped(Relay.ConnectionError);
                if (relayUiError != null) ImGui.TextWrapped(relayUiError);
                if (this.Plugin.Relay?.Fingerprint is { Length: > 0 } fingerprint) {
                    ImGui.TextWrapped(this.T("配对指纹（请与客户端核对）：", "Pairing fingerprint (compare with the client): ") + fingerprint);
                }
                if (relayInvitationTask is { IsCompleted: true }) {
                    try { relayInvitation = relayInvitationTask.GetAwaiter().GetResult(); relayUiError = null; }
                    catch (Exception ex) { relayUiError = ex.Message; }
                    relayInvitationTask = null;
                }
                ImGui.BeginDisabled(status != ConnectionStatus.Connected || relayInvitationTask != null);
                if (ImGui.Button(this.T("生成一次性邀请（10 分钟）", "Create one-time invitation (10 minutes)") + "##relay-invite")) {
                    relayInvitation = null; relayInvitationTask = this.Plugin.Relay!.CreateInvitationAsync();
                }
                ImGui.EndDisabled();
                if (relayInvitation != null && ImGui.Button(this.T("复制邀请到剪贴板", "Copy invitation to clipboard") + "##relay-copy")) ImGui.SetClipboardText(relayInvitation);
                ImGui.TextWrapped(this.T("在客户端新增连接时选择“自建中继”，粘贴邀请并核对指纹。生成新邀请会使旧邀请失效。", "Choose Self-hosted relay when adding a client connection, paste the invitation and compare the fingerprint. Creating a new invitation invalidates the previous one."));
            }
            if (ImGui.CollapsingHeader(this.T("服务器公钥", "Server public key") + "###public-key")) {
                var key = this.Plugin.Config.KeyPair!.PublicKey;
                var hex = key.ToHexString(true);
                ImGui.TextUnformatted(hex); DrawColours(key, hex);
                if (ImGui.Button(this.T("复制", "Copy") + "##copy-key")) ImGui.SetClipboardText(hex);
                ImGui.SameLine();
                if (ImGui.Button(this.T("重新生成", "Regenerate") + "##regenerate-key")) {
                    this.Plugin.Server.RegenerateKeyPair(); this.Plugin.Relay?.ResendPublicKey();
                }
                ImGui.TextWrapped(this.T("重新生成后，客户端需要重新核对服务器身份。", "After regeneration, clients must verify the server identity again."));
            }
        }

        private string RelayStatus(ConnectionStatus status) => status switch {
            ConnectionStatus.Connecting => this.T("正在连接", "Connecting"),
            ConnectionStatus.Negotiating => this.T("正在协商", "Negotiating"),
            ConnectionStatus.Connected => this.T("已连接", "Connected"),
            _ => this.T("已断开", "Disconnected"),
        };

        private void DrawMessages() {
            var backlog = this.Plugin.Config.BacklogEnabled;
            if (ImGui.Checkbox(this.T("保存插件内存历史", "Keep plugin memory history") + "##backlog", ref backlog)) {
                this.Plugin.Config.BacklogEnabled = backlog; this.SaveMessageSettings();
            }
            ImGui.TextWrapped(this.T("关闭会立即清空插件内存历史。客户端已有的本地历史不受影响。", "Disabling immediately clears plugin memory history. Existing local client history is unaffected."));
            var count = (int)this.Plugin.Config.BacklogCount;
            if (ImGui.InputInt(this.T("最多记录条数", "Maximum history messages") + "##history-count", ref count)) {
                this.Plugin.Config.BacklogCount = (ushort)Math.Clamp(count, 0, ushort.MaxValue); this.SaveMessageSettings();
            }
            var mib = this.Plugin.Config.BacklogMaxMiB;
            if (ImGui.InputInt(this.T("历史容量上限 (MiB)", "History size limit (MiB)") + "##history-bytes", ref mib)) {
                this.Plugin.Config.BacklogMaxMiB = Math.Clamp(mib, 1, 256); this.SaveMessageSettings();
            }
            var battle = this.Plugin.Config.SendBattle;
            if (ImGui.Checkbox(this.T("记录并发送战斗消息", "Record and send battle messages") + "##battle", ref battle)) {
                this.Plugin.Config.SendBattle = battle; this.SaveMessageSettings();
            }
            ImGui.TextWrapped(this.T("战斗过滤仅影响新消息；历史中的已有记录保留。其他频道按客户端历史、视图和通知需要订阅。", "Battle filtering affects new messages; existing history remains. Other channels follow client history, view and notification subscriptions."));
            var input = this.Plugin.Config.MessagesCountAsInput;
            if (ImGui.Checkbox(this.T("发送消息重置离席计时", "Sending messages resets the AFK timer") + "##input", ref input)) {
                this.Plugin.Config.MessagesCountAsInput = input; this.Plugin.Config.Save();
            }
        }
        private void SaveMessageSettings() { this.Plugin.Server.ApplyMessageSettings(); this.Plugin.Config.Save(); }

        private void DrawDevices() {
            TextWhite(this.T("已连接设备", "Connected devices"));
            var clients = this.Plugin.Server.Clients.Where(c => c.Value.Ready).ToArray();
            if (clients.Length == 0) ImGui.TextUnformatted(this.T("无", "None"));
            foreach (var entry in clients) {
                var trusted = this.Plugin.Config.TrustedKeys.Values.FirstOrDefault(k => entry.Value.Handshake != null && k.Item2.SequenceEqual(entry.Value.Handshake.RemotePublicKey));
                ImGui.TextUnformatted((trusted?.Item1 ?? this.T("未命名", "Unnamed")) + " · " + entry.Value.Remote);
                ImGui.SameLine();
                if (ImGui.Button(this.T("断开", "Disconnect") + "##" + entry.Key)) this.Plugin.Server.RemoveClient(entry.Key);
            }
            ImGui.Separator();
            TextWhite(this.T("已信任设备", "Trusted devices"));
            if (this.Plugin.Config.TrustedKeys.Count == 0) ImGui.TextUnformatted(this.T("无", "None"));
            foreach (var entry in this.Plugin.Config.TrustedKeys.ToArray()) {
                ImGui.TextUnformatted(entry.Value.Item1);
                if (ImGui.IsItemHovered()) {
                    ImGui.BeginTooltip(); ImGui.TextUnformatted(entry.Value.Item2.ToHexString(true)); ImGui.EndTooltip();
                }
                ImGui.SameLine();
                if (ImGui.Button(this.T("取消信任", "Untrust") + "##" + entry.Key)) {
                    this.Plugin.Config.TrustedKeys.TryRemove(entry.Key, out _); this.Plugin.Config.Save();
                }
            }
        }

        private void DrawDiagnostics() {
            var server = this.Plugin.Server;
            ImGui.TextUnformatted(this.T("监听：", "Listener: ") + (server.Running ? this.T("运行中", "Running") : this.T("已停止", "Stopped")) + " · " + this.Plugin.Config.Port);
            ImGui.TextUnformatted(this.T("设备（含协商中）：", "Devices (including handshakes): ") + server.Clients.Count + " / 32");
            var history = server.BacklogUsage;
            ImGui.TextUnformatted(this.T("内存历史：", "Memory history: ") + $"{history.Count} · {history.Bytes / 1048576d:F2} MiB");
            var game = server.GameQueueUsage;
            ImGui.TextUnformatted(this.T("待交给游戏：", "Pending game commands: ") + $"{game.Count} / 128 · {game.Bytes} B");
            var outgoing = server.Clients.Values.Select(c => c.Queue.Usage).ToArray();
            ImGui.TextUnformatted(this.T("发送积压（含正在写入）：", "Outgoing backlog (including active writes): ") + $"{outgoing.Sum(q => q.Count)} · {outgoing.Sum(q => q.Bytes) / 1048576d:F2} MiB");
            ImGui.TextWrapped(this.T("每个设备最多 512 个包、8 MiB；单次写入最多等待 10 秒。超限会断开该设备并取消其待发送内容。", "Each device allows 512 packets and 8 MiB, with a 10-second write timeout. Exceeding a limit disconnects that device and cancels its pending commands."));
            var cache = server.CacheUsage;
            ImGui.TextUnformatted(this.T("物品 / 地图缓存：", "Item / map cache: ") + $"{cache.Items} / {cache.Maps}");
            ImGui.TextUnformatted(this.T("缓存命中 / 未命中：", "Cache hits / misses: ") + $"{cache.Hits} / {cache.Misses}");
            ImGui.Separator();
            ImGui.TextWrapped(this.T("最近连接错误：", "Last connection error: ") + (server.LastError ?? this.T("无", "None")));
        }
        private static void DrawColours(byte[] bytes, string widthOf) {
            DrawColours(bytes, ImGui.CalcTextSize(widthOf).X);
        }

        private static void DrawColours(byte[] bytes, float width = 0f) {
            var pos = ImGui.GetCursorScreenPos();
            var spacing = ImGui.GetStyle().ItemSpacing;

            var colours = bytes.ToColours();

            var sizeX = width == 0f ? 32f : width / colours.Count;

            for (var i = 0; i < colours.Count; i++) {
                var topLeft = new Vector2(
                    pos.X + (sizeX * i),
                    pos.Y + spacing.Y
                );
                var bottomRight = new Vector2(
                    pos.X + (sizeX * (i + 1)),
                    pos.Y + spacing.Y + 16
                );

                ImGui.GetWindowDrawList().AddRectFilled(
                    topLeft,
                    bottomRight,
                    ImGui.GetColorU32(colours[i])
                );
            }

            // create a spacing for 32px and spacing
            ImGui.Dummy(new Vector2(0, 16 + spacing.Y * 2));
        }

        public void OpenSettings() {
            this.ShowSettings = true;
        }

        private void AcceptPending() {
            while (this.Plugin.Server.PendingClients.Reader.TryRead(out var item)) {
                this._pending[Guid.NewGuid()] = item;
            }
        }

        private bool DrawPending(Guid id, BaseClient client, Channel<bool, bool> accepted) {
            var ret = false;

            var clientPublic = client.Handshake!.RemotePublicKey;
            var clientPublicHex = clientPublic.ToHexString(upper: true);
            var serverPublic = this.Plugin.Config.KeyPair!.PublicKey;
            var serverPublicHex = serverPublic.ToHexString(upper: true);

            var width = Math.Max(ImGui.CalcTextSize(clientPublicHex).X, ImGui.CalcTextSize(serverPublicHex).X) + (ImGui.GetStyle().WindowPadding.X * 2);

            if (!Begin(this.T("新设备连接请求", "Incoming XIVChat connection") + "##" + id, ImGuiWindowFlags.AlwaysAutoResize)) {
                ImGui.End();
                return false;
            }

            ImGui.PushTextWrapPos(width);

            ImGui.TextUnformatted(this.T("一个新设备请求连接 XIVChat。若是你的设备，请核对以下两个公钥与客户端显示的一致。", "A new device is requesting access to XIVChat. If it is yours, verify both public keys match the client."));

            ImGui.Separator();

            TextWhite(this.T("服务器", "Server"));
            ImGui.TextUnformatted(serverPublicHex);
            DrawColours(serverPublic, serverPublicHex);

            ImGui.Spacing();

            TextWhite(this.T("客户端", "Client"));
            ImGui.TextUnformatted(clientPublicHex);
            DrawColours(clientPublic, clientPublicHex);

            ImGui.Separator();

            ImGui.TextUnformatted(this.T("确认信任后，可以给设备起一个便于辨认的名字。", "Give this device a recognizable name if you trust it."));

            ImGui.PopTextWrapPos();

            if (!this._pendingNames.TryGetValue(id, out var name)) {
                name = this.T("未命名", "Unnamed");
            }

            if (WithWhiteText(() => ImGui.InputText(this.T("设备名称", "Device name") + "##client-name", ref name, 100, ImGuiInputTextFlags.AutoSelectAll))) {
                this._pendingNames[id] = name;
            }

            ImGui.Separator();

            ImGui.TextUnformatted(this.T("两个公钥是否都一致？", "Do both keys match?"));
            if (WithWhiteText(() => ImGui.Button(this.T("是，信任", "Yes, trust")))) {
                accepted.Writer.TryWrite(true);
                this.Plugin.Config.TrustedKeys[Guid.NewGuid()] = Tuple.Create(name, client.Handshake.RemotePublicKey);
                this.Plugin.Config.Save();
                this._pendingNames.Remove(id);
                ret = true;
            }

            ImGui.SameLine();
            if (WithWhiteText(() => ImGui.Button(this.T("否，拒绝", "No, reject")))) {
                accepted.Writer.TryWrite(false);
                this._pendingNames.Remove(id);
                ret = true;
            }

            ImGui.End();

            return ret;
        }
    }
}
