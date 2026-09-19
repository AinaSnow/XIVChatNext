using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;

namespace XIVChatCommon.Presentation;

// Local presentation settings, never part of the chat wire protocol.
public sealed class PrivacySettings {
    public bool Enabled { get; set; }
    public bool HideSelf { get; set; } = true;
    public bool HideOthers { get; set; } = true;
    public string SelfName { get; set; } = "";
    public string Seed { get; set; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public void Validate() {
        SelfName = NormalizeName(SelfName);
        if (Convert.FromBase64String(Seed).Length != 32) throw new ArgumentException("Invalid privacy seed.");
        if (Enabled && !HideSelf && !HideOthers) HideSelf = HideOthers = true;
    }
    public PrivacySettings Copy() => new() { Enabled = Enabled, HideSelf = HideSelf, HideOthers = HideOthers, SelfName = SelfName, Seed = Seed };
    public static string NormalizeName(string? value) {
        value = (value ?? "").Trim();
        if (value.Length > 64 || value.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'))
            throw new ArgumentException("Display names must be a single line of at most 64 characters.");
        return value;
    }
}

public sealed record DisplayContext(string Source, string OwnerKey, CharacterIdentity? Owner = null);
public sealed record ContactDisplayProfile(string Source, string OwnerKey, string PeerKey, CharacterIdentity Peer,
    string Nickname = "", string Pseudonym = "");
public sealed record DisplayIdentity(string Name, string World, bool Hidden) {
    public string Label => string.IsNullOrEmpty(World) ? Name : Name + " @ " + World;
}

/// <summary>
/// Thread-safe, UI-independent projection of original identities. It never changes messages,
/// routing identities or stored history. Export workers use an isolated snapshot.
/// </summary>
public sealed class IdentityDisplay {
    private readonly object gate = new();
    private PrivacySettings settings;
    private bool chinese;
    private readonly Dictionary<(string Source, string Owner, string Peer), ContactDisplayProfile> profiles = new();
    private readonly Dictionary<(string Source, string Owner), Dictionary<string, CharacterIdentity>> known = new();
    private readonly Dictionary<(string Source, string Owner), CharacterIdentity> owners = new();
    private readonly Dictionary<(string Source, string Owner), Rules> rules = new();
    public long Revision { get; private set; }
    private sealed record Rules(Regex? Pattern, Dictionary<string, string> Replacements);
    private sealed record Edit(int Start, int Length, string Text);
    public event Action<ContactDisplayProfile>? ProfileGenerated;
    public bool Enabled { get { lock (gate) return settings.Enabled; } }
    public string Anonymous { get { lock (gate) return chinese ? "匿名玩家" : "Anonymous player"; } }

    public IdentityDisplay(PrivacySettings settings, bool chinese = false) {
        this.settings = settings.Copy(); this.settings.Validate(); this.chinese = chinese;
    }
    public void Configure(PrivacySettings value, bool useChinese) {
        var copy = value.Copy(); copy.Validate();
        lock (gate) { settings = copy; chinese = useChinese; rules.Clear(); }
    }
    public IdentityDisplay Snapshot() {
        lock (gate) {
            var snapshot = new IdentityDisplay(settings, chinese);
            foreach (var profile in profiles.Values) snapshot.Restore(profile);
            foreach (var pair in owners) snapshot.owners[pair.Key] = ConversationIdentity.Copy(pair.Value);
            foreach (var pair in known) snapshot.known[pair.Key] = pair.Value.ToDictionary(p => p.Key, p => ConversationIdentity.Copy(p.Value));
            return snapshot;
        }
    }
    public DisplayContext Context(string source, string? ownerKey, CharacterIdentity? owner = null) {
        lock (gate) {
            var key = ownerKey ?? owner?.Key ?? "unassigned:" + source;
            if (owner != null) { owners[(source, key)] = ConversationIdentity.Copy(owner); RegisterCore(new(source, key, owner), owner); }
            else owners.TryGetValue((source, key), out owner);
            return new(source, key, owner);
        }
    }
    public void Register(DisplayContext context, CharacterIdentity? identity) {
        lock (gate) {
            if (context.Owner != null) { owners[(context.Source, context.OwnerKey)] = ConversationIdentity.Copy(context.Owner); RegisterCore(context, context.Owner); }
            RegisterCore(context, identity);
        }
    }
    private void RegisterCore(DisplayContext context, CharacterIdentity? identity) {
        if (identity == null || string.IsNullOrWhiteSpace(identity.Name)) return;
        var scope = (context.Source, context.OwnerKey);
        if (!known.TryGetValue(scope, out var people)) known[scope] = people = new();
        var key = ConversationIdentity.PeerKey(identity) ?? "unknown:" + identity.Name.Trim().ToUpperInvariant();
        if (people.TryGetValue(key, out var prior) && prior.Name == identity.Name && prior.HomeWorld == identity.HomeWorld && prior.ContentId == identity.ContentId) return;
        var copy = ConversationIdentity.Copy(identity);
        if (string.IsNullOrEmpty(copy.HomeWorld) && prior != null) copy.HomeWorld = prior.HomeWorld;
        if (copy.ContentId == 0 && prior != null) copy.ContentId = prior.ContentId;
        if (prior != null && prior.Name == copy.Name && prior.HomeWorld == copy.HomeWorld && prior.ContentId == copy.ContentId) return;
        people[key] = copy; rules.Remove(scope); Revision++;
    }
    public void Restore(ContactDisplayProfile profile) {
        if (ConversationIdentity.PeerKey(profile.Peer) != profile.PeerKey) throw new ArgumentException("Invalid contact identity.");
        PrivacySettings.NormalizeName(profile.Nickname);
        if (profile.Pseudonym.Length > 64 || profile.Pseudonym.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Invalid pseudonym token.");
        lock (gate) {
            profiles[(profile.Source, profile.OwnerKey, profile.PeerKey)] = profile with { Peer = ConversationIdentity.Copy(profile.Peer) };
            RegisterCore(Context(profile.Source, profile.OwnerKey), profile.Peer); rules.Remove((profile.Source, profile.OwnerKey));
        }
    }
    public string Nickname(DisplayContext context, CharacterIdentity peer) {
        lock (gate) return profiles.GetValueOrDefault((context.Source, context.OwnerKey, ConversationIdentity.PeerKey(peer) ?? ""))?.Nickname ?? "";
    }
    public ContactDisplayProfile WithNickname(DisplayContext context, CharacterIdentity peer, string nickname) {
        var key = ConversationIdentity.PeerKey(peer) ?? throw new ArgumentException("A complete character identity is required.");
        lock (gate) return new(context.Source, context.OwnerKey, key, ConversationIdentity.Copy(peer), PrivacySettings.NormalizeName(nickname),
            profiles.GetValueOrDefault((context.Source, context.OwnerKey, key))?.Pseudonym ?? "");
    }
    private bool IsSelf(DisplayContext context, CharacterIdentity peer) =>
        peer.Key != null && peer.Key == context.OwnerKey || context.Owner != null &&
        (peer.ContentId != 0 && peer.ContentId == context.Owner.ContentId ||
         ConversationIdentity.PeerKey(peer) is { } key && key == ConversationIdentity.PeerKey(context.Owner) ||
         peer.HomeWorldId == 0 && string.Equals(peer.Name, context.Owner.Name, StringComparison.OrdinalIgnoreCase));
    public bool Hidden(DisplayContext context, CharacterIdentity? peer) {
        lock (gate) {
            if (!settings.Enabled) return false;
            if (peer == null) return settings.HideSelf || settings.HideOthers;
            if (peer.HomeWorldId == 0 && known.TryGetValue((context.Source, context.OwnerKey), out var people)) {
                var matches = people.Values.Where(p => p.IsComplete && string.Equals(p.Name, peer.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length > 1) return matches.Any(p => IsSelf(context, p) ? settings.HideSelf : settings.HideOthers);
            }
            return IsSelf(context, peer) ? settings.HideSelf : settings.HideOthers;
        }
    }
    public DisplayIdentity Identity(DisplayContext context, CharacterIdentity? peer, bool nickname = true) {
        lock (gate) {
            Register(context, peer);
            bool hidden = Hidden(context, peer);
            if (peer == null || string.IsNullOrWhiteSpace(peer.Name)) return new(Anonymous, "", hidden);
            if (!hidden) return new(nickname && Nickname(context, peer) is { Length: > 0 } note ? note : peer.Name, peer.HomeWorld, false);
            if (peer.HomeWorldId == 0 && known.TryGetValue((context.Source, context.OwnerKey), out var people) &&
                people.Values.Count(p => p.IsComplete && string.Equals(p.Name, peer.Name, StringComparison.OrdinalIgnoreCase)) > 1)
                return new(Anonymous, "", true);
            if (IsSelf(context, peer)) return new(settings.SelfName.Length > 0 ? settings.SelfName : chinese ? "我" : "Me", "", true);
            if (ConversationIdentity.PeerKey(peer) is not { } key) return new(Anonymous, "", true);
            var token = Pseudonym(context, peer, key);
            string[] zh = ["晴空", "松风", "白桦", "星灯", "溪流", "月光", "晨露", "飞鸟"];
            string[] en = ["Sky", "Breeze", "Birch", "Star", "Brook", "Moon", "Dew", "Bird"];
            int index = Convert.ToInt32(token[..2], 16) % zh.Length;
            return new((chinese ? zh[index] : en[index]) + "-" + token, "", true);
        }
    }
    private string Pseudonym(DisplayContext context, CharacterIdentity peer, string key) {
        var profileKey = (context.Source, context.OwnerKey, key);
        var profile = profiles.GetValueOrDefault(profileKey);
        if (profile?.Pseudonym is { Length: >= 8 } saved) return saved;
        // Length prefixes prevent ambiguity between source, owner and peer components.
        var input = string.Concat(new[] { context.Source, context.OwnerKey, key }.Select(p => p.Length + ":" + p));
        var digest = Convert.ToHexString(HMACSHA256.HashData(Convert.FromBase64String(settings.Seed), Encoding.UTF8.GetBytes(input)));
        var token = digest[..8];
        for (int length = 12; profiles.Values.Any(p => p.Source == context.Source && p.OwnerKey == context.OwnerKey && p.PeerKey != key && p.Pseudonym == token); length += 4) {
            if (length > digest.Length) throw new InvalidOperationException("Pseudonym collision.");
            token = digest[..length];
        }
        profile = new(context.Source, context.OwnerKey, key, ConversationIdentity.Copy(peer), profile?.Nickname ?? "", token);
        profiles[profileKey] = profile; ProfileGenerated?.Invoke(profile);
        return token;
    }
    public DisplayContext Observe(string source, ServerMessage message) {
        lock (gate) {
            var context = Context(source, message.Owner?.Key, message.Owner);
            Register(context, message.TellPeer); Register(context, Sender(context, message)); return context;
        }
    }
    public CharacterIdentity? Sender(DisplayContext context, ServerMessage message) {
        lock (gate) {
            ServerMessage.SenderPlayer? sender;
            try { sender = message.GetSenderPlayer(); }
            catch (Exception ex) when (ex is EndOfStreamException or IOException or ArgumentException or OverflowException) { return null; }
            if (sender == null) return null;
            var directed = ((ushort)message.Channel & 127) switch {
                (ushort)ChatType.TellIncoming => message.TellPeer,
                (ushort)ChatType.TellOutgoing => message.Owner,
                _ => null,
            };
            if (directed != null && string.Equals(directed.Name, sender.Name, StringComparison.OrdinalIgnoreCase) &&
                (sender.Server == 0 || directed.HomeWorldId == sender.Server)) return directed;
            var candidates = new[] { message.Owner, message.TellPeer }.Where(p => p != null)
                .Concat(known.GetValueOrDefault((context.Source, context.OwnerKey))?.Values.Cast<CharacterIdentity?>() ?? Enumerable.Empty<CharacterIdentity?>())
                .Where(p => string.Equals(p!.Name, sender.Name, StringComparison.OrdinalIgnoreCase) && (sender.Server == 0 || p.HomeWorldId == sender.Server))
                .DistinctBy(p => ConversationIdentity.PeerKey(p)).ToArray();
            if (candidates.Length == 1) return candidates[0];
            return new CharacterIdentity { Name = sender.Name, HomeWorldId = sender.Server };
        }
    }
    private Rules GetRules(DisplayContext context) {
        var scope = (context.Source, context.OwnerKey);
        if (rules.TryGetValue(scope, out var cached)) return cached;
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (known.TryGetValue(scope, out var people)) {
            foreach (var group in people.Values.ToArray().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)) {
                var identities = group.ToArray();
                if (!identities.Any(p => Hidden(context, p))) continue;
                var labels = identities.Select(p => Identity(context, p, false).Name).Distinct().ToArray();
                replacements[group.Key] = labels.Length == 1 ? labels[0] : Anonymous;
                foreach (var peer in identities.Where(p => Hidden(context, p) && !string.IsNullOrEmpty(p.HomeWorld))) {
                    var label = Identity(context, peer, false).Name;
                    replacements[peer.Name + "@" + peer.HomeWorld] = label;
                    replacements[peer.Name + " @ " + peer.HomeWorld] = label;
                    replacements[peer.Name + "\ue0bb" + peer.HomeWorld] = label;
                }
            }
        }
        // Escaped literals, longest first. No attacker-controlled regular expressions.
        var pattern = replacements.Count == 0 ? null : new Regex(string.Join("|", replacements.Keys.OrderByDescending(n => n.Length).Select(Regex.Escape)),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return rules[scope] = new(pattern, replacements);
    }
    private static bool NameChar(char c) => char.IsLetterOrDigit(c) || c is '\'' or '-' ||
        char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark;
    private static bool Cjk(char c) => c is >= '\u2e80' and <= '\u9fff' or >= '\uf900' and <= '\ufaff' or >= '\uff66' and <= '\uff9f';
    private static bool GameText(ServerMessage message) => (ChatType)((ushort)message.Channel & 127) is
        ChatType.Damage or ChatType.Miss or ChatType.Action or ChatType.Item or ChatType.Healing or
        ChatType.GainBuff or ChatType.GainDebuff or ChatType.LoseBuff or ChatType.LoseDebuff or
        ChatType.System or ChatType.BattleSystem or ChatType.GatheringSystem or ChatType.LootNotice or
        ChatType.Progress or ChatType.LootRoll or ChatType.Crafting or ChatType.Gathering or ChatType.StandardEmote;
    private static bool ContinuesName(char adjacent, char edge, bool gameText) {
        if (!NameChar(adjacent)) return false;
        // Japanese game text joins Latin names directly to particles ("Alice Snowの攻撃").
        // A switch from a Latin name to CJK prose is a boundary, unlike a longer Latin name.
        bool latinEdge = char.IsLetter(edge) && (edge <= '\u024f' || edge is >= '\u1e00' and <= '\u1eff');
        return !(Cjk(adjacent) && (latinEdge || gameText && Cjk(edge)));
    }
    // These are actor slots in generated Chinese combat messages, not arbitrary chat.
    // Combat logs often have no sender/player payload; unknown actors must not leak
    // simply because they have never chatted. Unknown actors use the neutral alias.
    private const string CombatNameChars = @"[\p{L}\p{M}\p{Nd}'’·・ .-]";
    private const string CombatName = CombatNameChars + "{1,64}?";
    private static readonly Regex[] CombatActors = {
        new(@"^[\s\uFFFC\uE000-\uF8FF→⇒►]*(?<actor>" + CombatName + @")(?=正在发动|发动了|发动[“「]|附加了|失去了|恢复了|的攻击)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
        new(@"^[\s\uFFFC\uE000-\uF8FF→⇒►]*对(?<actor>" + CombatName + @")(?=附加了|造成了|恢复了)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
        new(@"^[\s\uFFFC\uE000-\uF8FF→⇒►]*(?<actor>" + CombatNameChars + @"{1,64})的(?=.+(?:状态效果消失了|效果消失了|状态效果延长了))", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
        new(@"对(?<actor>" + CombatName + @")(?=造成了\d|恢复了\d)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
    };
    private IEnumerable<Edit> CombatEdits(DisplayContext context, string text, ServerMessage message) {
        if (((ushort)message.Channel & 127) is < 41 or > 49) yield break;
        foreach (var pattern in CombatActors) foreach (Match match in pattern.Matches(text)) {
            var slot = match.Groups["actor"];
            var name = slot.Value.Trim();
            if (name.Length == 0) continue;
            var candidates = known.GetValueOrDefault((context.Source, context.OwnerKey))?.Values
                .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray() ?? Array.Empty<CharacterIdentity>();
            bool hidden = candidates.Length == 0 ? settings.HideOthers : candidates.Any(p => Hidden(context, p));
            string replacement = name;
            if (hidden) {
                var labels = candidates.Select(p => Identity(context, p, false).Name).Distinct().ToArray();
                replacement = labels.Length == 1 ? labels[0] : Anonymous;
            }
            // Include visible actors as well: a longer name must win over a shorter
            // known name that happens to be its prefix, including self-only mode.
            yield return new(slot.Index + slot.Value.IndexOf(name, StringComparison.Ordinal), name.Length, replacement);
        }
    }
    private List<Edit> Edits(DisplayContext context, string text, ServerMessage? message = null) {
        var result = new List<Edit>();
        if (!settings.Enabled || text.Length == 0) return result;
        var rule = GetRules(context);
        try {
            if (rule.Pattern != null) foreach (Match match in rule.Pattern.Matches(text)) {
                bool gameText = message != null && GameText(message);
                if (match.Index > 0 && ContinuesName(text[match.Index - 1], match.Value[0], gameText) ||
                    match.Index + match.Length < text.Length && ContinuesName(text[match.Index + match.Length], match.Value[^1], gameText)) continue;
                result.Add(new(match.Index, match.Length, rule.Replacements[match.Value]));
            }
            if (message != null) foreach (var actor in CombatEdits(context, text, message)) {
                result.RemoveAll(e => e.Start < actor.Start + actor.Length && actor.Start < e.Start + e.Length);
                result.Add(actor);
            }
        } catch (RegexMatchTimeoutException) { return new() { new(0, text.Length, Anonymous) }; }
        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }
    private static string Apply(string text, IEnumerable<Edit> edits, int start, int length) {
        var result = new StringBuilder(); int cursor = start, end = start + length;
        foreach (var edit in edits) {
            if (edit.Start >= end || edit.Start + edit.Length <= start) continue;
            if (edit.Start > cursor) result.Append(text, cursor, edit.Start - cursor);
            if (edit.Start >= start) result.Append(edit.Text);
            cursor = Math.Min(end, edit.Start + edit.Length);
        }
        if (cursor < end) result.Append(text, cursor, end - cursor);
        return result.ToString();
    }
    public string Text(DisplayContext context, string? text) {
        lock (gate) {
            text ??= ""; Register(context, null);
            return Apply(text, Edits(context, text), 0, text.Length);
        }
    }
    public string Content(string source, ServerMessage message) {
        lock (gate) {
            var context = Observe(source, message);
            var text = message.ContentText;
            return Apply(text, Edits(context, text, message), 0, text.Length);
        }
    }
    public string SenderLabel(DisplayContext context, ServerMessage message, bool nickname = true) {
        lock (gate) {
            var sender = Sender(context, message);
            if (sender == null) return settings.Enabled && !string.IsNullOrEmpty(message.SenderText) ? Anonymous : message.SenderText;
            return Identity(context, sender, nickname).Name;
        }
    }
    public IReadOnlyList<Chunk> Chunks(string source, ServerMessage message) {
        lock (gate) {
            var context = Observe(source, message);
            // Icon separators prevent joining characters across an actual visible icon.
            var text = string.Concat(message.Chunks.Select(c => c is TextChunk t ? t.Content : "\ufffc"));
            var edits = Edits(context, text, message);
            var sender = Sender(context, message);
            if (sender != null && !Hidden(context, sender)) {
                var name = Identity(context, sender).Name;
                int body = text.EndsWith(message.ContentText, StringComparison.Ordinal) ? text.Length - message.ContentText.Length : 0;
                int at = text.IndexOf(sender.Name, StringComparison.OrdinalIgnoreCase);
                if (name != sender.Name && at >= 0 && at + sender.Name.Length <= body && !edits.Any(e => e.Start < at + sender.Name.Length && e.Start + e.Length > at))
                    edits.Add(new(at, sender.Name.Length, name));
            }
            edits.Sort((a, b) => a.Start.CompareTo(b.Start));
            var chunks = new List<Chunk>(); int offset = 0;
            foreach (var chunk in message.Chunks) {
                if (chunk is TextChunk part) { chunks.Add(part.WithContent(Apply(text, edits, offset, part.Content.Length))); offset += part.Content.Length; }
                else { chunks.Add(chunk); offset++; }
            }
            return chunks;
        }
    }
    public string ExportLine(string source, ServerMessage message, bool timestamps) {
        lock (gate) {
            var context = Observe(source, message);
            var sender = settings.Enabled ? Text(context, message.SenderText) : message.SenderText;
            // Missing sender metadata must not make an identity label bypass protection.
            if (settings.Enabled && Sender(context, message) is { } peer && Hidden(context, peer)) sender = Identity(context, peer, false).Name;
            else if (settings.Enabled && sender.Length > 0 && Sender(context, message) == null) sender = Anonymous;
            return (timestamps ? message.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " " : "") +
                "[" + message.Channel + "] " + (sender.Length == 0 ? "" : sender + ": ") + Content(source, message);
        }
    }
}
