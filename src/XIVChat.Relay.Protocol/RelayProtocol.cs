using System.Text.Json;

namespace XIVChat.Relay.Protocol;

public static class RelayProtocol {
    public const int Version = 1;
    public const int MaxControlBytes = 16 * 1024;
    public const int MaxDataBytes = 64 * 1024;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static Uri BaseUri(string address) {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            throw new ArgumentException("Use an HTTPS server address without a path, query or credentials. HTTP is allowed only on loopback for local testing.");
        return uri;
    }
    public static Uri Endpoint(string address, string path, bool websocket = false) {
        var builder = new UriBuilder(new Uri(BaseUri(address), path));
        if (websocket) builder.Scheme = builder.Scheme == "https" ? "wss" : "ws";
        return builder.Uri;
    }
    public static bool ValidFingerprint(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
}

public sealed record HostHello(int Version, string Fingerprint);
public sealed record ControlMessage(string Kind, string? SessionId = null, string? Ticket = null);
public sealed record Invitation(string Server, string Code, string Fingerprint, DateTimeOffset ExpiresAt, int Version = RelayProtocol.Version);
public sealed record PairRequest(string Code, string Name, int Version = RelayProtocol.Version);
public sealed record PairResult(string DeviceId, string ClientId, string Credential, string Fingerprint);
public sealed record RelayProfile(string Server, string DeviceId, string ClientId, string ProtectedCredential, string Fingerprint);
