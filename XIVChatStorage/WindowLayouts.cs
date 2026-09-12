using System.Text.Json;
using XIVChatCommon.Message;

namespace XIVChatStorage;

public sealed record WindowBounds(int X = 80, int Y = 80, int Width = 640, int Height = 640) {
    public WindowBounds Fit(WindowBounds work) {
        var w = Math.Clamp(Width, Math.Min(360, work.Width), work.Width);
        var h = Math.Clamp(Height, Math.Min(300, work.Height), work.Height);
        return new(Math.Clamp(X, work.X, work.X + work.Width - w), Math.Clamp(Y, work.Y, work.Y + work.Height - h), w, h);
    }
}
public sealed record ChatWindowState {
    public string Source { get; init; } = "";
    public string OwnerKey { get; init; } = "";
    public CharacterIdentity? Peer { get; init; }
    public CharacterIdentity? Owner { get; init; }
    public string? ChannelId { get; init; }
    public string Name { get; init; } = "";
    public string Draft { get; set; } = "";
    public string? FailedDraft { get; set; }
    public string? PendingDraft { get; set; }
    public bool Open { get; set; }
    public WindowBounds Bounds { get; set; } = new();
    public bool Topmost { get; set; }
    public bool Markdown { get; set; } = true;
    public bool Timestamps { get; set; } = true;
    public double FontSize { get; set; } = 14;
    public bool FollowingLatest { get; set; } = true;
    public string? ScrollId { get; set; }
    public string Key => JsonSerializer.Serialize(new[] { Source, OwnerKey, ChannelId == null ? "conversation" : "channel", ChannelId ?? ConversationIdentity.PeerKey(Peer) ?? "" });
    public bool Valid => double.IsFinite(FontSize) && FontSize is >= 8 and <= 48 && Source is { Length: > 0 and <= 2048 } && OwnerKey is { Length: > 0 and <= 512 } &&
        ((ChannelId != null && ChannelId.Length is > 0 and <= 128) ^ ConversationIdentity.PeerKey(Peer) != null) && Draft is { Length: <= 16384 } &&
        (Owner == null || Owner.Key == OwnerKey && Owner.Name.Length <= 128 && Owner.HomeWorld.Length <= 64) &&
        (Peer == null || Peer.Name.Length <= 128 && Peer.HomeWorld.Length <= 64) && (PendingDraft?.Length ?? 0) <= 16384 && (FailedDraft?.Length ?? 0) <= 16384 && Name is { Length: <= 256 } && (ScrollId?.Length ?? 0) <= 4096;
}
public sealed record WorkspaceLayout {
    public int Version { get; init; } = 1;
    public bool MainVisible { get; set; } = true;
    public WindowBounds MainBounds { get; set; } = new(80, 80, 1240, 820);
    public List<ChatWindowState> Windows { get; set; } = new();
    public Dictionary<string, string> MainDrafts { get; set; } = new();
    public ChatWindowState? MainView { get; set; }
    public string MainSection { get; set; } = "channels";
    public static WorkspaceLayout Parse(string json) {
        if (json.Length > 1048576) throw new InvalidDataException("Layout is too large.");
        var result = JsonSerializer.Deserialize<WorkspaceLayout>(json) ?? throw new InvalidDataException("Empty layout.");
        if (result.Version != 1 || result.Windows == null || result.Windows.Count > 64 || result.MainBounds == null ||
            result.MainDrafts == null || result.MainDrafts.Count > 256 ||
            result.MainDrafts.Any(d => d.Key.Length > 2048 || d.Value == null || d.Value.Length > 16384) ||
            result.MainView != null && !result.MainView.Valid ||
            result.MainSection is not ("channels" or "conversations" or "friends" or "history" or "events" or "favorites") ||
            result.Windows.Any(w => w == null || !w.Valid || w.Bounds == null) || result.Windows.Count(w => w.Open) > 12 ||
            result.Windows.Select(w => w.Key).Distinct().Count() != result.Windows.Count)
            throw new InvalidDataException("Invalid layout.");
        return result;
    }
    public string Serialize() { var json = JsonSerializer.Serialize(this); Parse(json); return json; }
}
public sealed partial class HistoryStore {
    public Task SaveLayoutAsync(string id, WorkspaceLayout layout) {
        if (id.Length is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(id));
        var json = layout.Serialize();
        return Enqueue((db, tx) => {
            using var cmd = Command(db, "INSERT INTO window_layouts(id,payload) VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload", tx, ("$id", id), ("$json", json));
            cmd.ExecuteNonQuery();
        });
    }
    public Task<WorkspaceLayout?> LoadLayoutAsync(string id) => Task.Run(() => {
        using var db = OpenReader();
        using var cmd = Command(db, "SELECT payload FROM window_layouts WHERE id=$id", null, ("$id", id));
        return cmd.ExecuteScalar() is string json ? WorkspaceLayout.Parse(json) : null;
    });
    public Task<IReadOnlyList<string>> GetLayoutNamesAsync() => Task.Run<IReadOnlyList<string>>(() => {
        using var db = OpenReader();
        using var cmd = Command(db, "SELECT id FROM window_layouts WHERE id<>'current' ORDER BY id LIMIT 50", null);
        using var reader = cmd.ExecuteReader(); var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0)); return names;
    });
}
