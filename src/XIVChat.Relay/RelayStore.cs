using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using XIVChat.Relay.Protocol;

namespace XIVChat.Relay;

public sealed record DeviceIdentity(string Id, string Name, string? Fingerprint);
public sealed record ClientIdentity(string Id, string DeviceId);
public sealed record ManagedDevice(string Id, string Name, string? Fingerprint, bool Revoked);
public sealed record ManagedClient(string Id, string DeviceId, string Name, bool Revoked);
public sealed class RelayStore : IDisposable {
    private readonly SqliteConnection database;
    private readonly object gate = new();
    public RelayStore(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); database.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; " +
            "CREATE TABLE IF NOT EXISTS devices(id TEXT PRIMARY KEY,name TEXT NOT NULL,token_hash TEXT UNIQUE NOT NULL,fingerprint TEXT,revoked INTEGER NOT NULL DEFAULT 0);" +
            "CREATE TABLE IF NOT EXISTS clients(id TEXT PRIMARY KEY,device_id TEXT NOT NULL REFERENCES devices(id),name TEXT NOT NULL,token_hash TEXT UNIQUE NOT NULL,revoked INTEGER NOT NULL DEFAULT 0);" +
            "CREATE TABLE IF NOT EXISTS invitations(token_hash TEXT PRIMARY KEY,device_id TEXT NOT NULL REFERENCES devices(id),expires INTEGER NOT NULL,fingerprint TEXT NOT NULL);");
    }
    public static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private SqliteCommand Command(string sql, params (string, object?)[] values) {
        var command = database.CreateCommand(); command.CommandText = sql;
        foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return command;
    }
    private int Execute(string sql, params (string, object?)[] values) { using var command = Command(sql, values); return command.ExecuteNonQuery(); }
    public (string Id, string Credential) AddDevice(string name) {
        ValidateName(name);
        lock (gate) {
            var id = Guid.NewGuid().ToString("N"); var secret = Secret();
            Execute("INSERT INTO devices(id,name,token_hash) VALUES($id,$name,$hash)", ("$id", id), ("$name", name), ("$hash", Hash(secret)));
            return (id, secret);
        }
    }
    public DeviceIdentity? Host(string token) {
        if (token.Length is < 32 or > 128) return null;
        lock (gate) {
            using var command = Command("SELECT id,name,fingerprint FROM devices WHERE token_hash=$hash AND revoked=0", ("$hash", Hash(token)));
            using var reader = command.ExecuteReader();
            return reader.Read() ? new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)) : null;
        }
    }
    public ClientIdentity? Client(string token) {
        if (token.Length is < 32 or > 128) return null;
        lock (gate) {
            using var command = Command("SELECT c.id,c.device_id FROM clients c JOIN devices d ON c.device_id=d.id WHERE c.token_hash=$hash AND c.revoked=0 AND d.revoked=0", ("$hash", Hash(token)));
            using var reader = command.ExecuteReader(); return reader.Read() ? new(reader.GetString(0), reader.GetString(1)) : null;
        }
    }
    public bool Register(string id, string fingerprint) {
        if (!RelayProtocol.ValidFingerprint(fingerprint)) return false;
        lock (gate) return Execute("UPDATE devices SET fingerprint=$fp WHERE id=$id AND revoked=0 AND (fingerprint IS NULL OR fingerprint=$fp)", ("$fp", fingerprint.ToUpperInvariant()), ("$id", id)) == 1;
    }
    public Invitation Invite(string id) {
        lock (gate) {
            using var command = Command("SELECT fingerprint FROM devices WHERE id=$id AND revoked=0", ("$id", id));
            var fingerprint = command.ExecuteScalar() as string ?? throw new InvalidOperationException("Device is not registered.");
            var secret = Secret(); var expiry = DateTimeOffset.UtcNow.AddMinutes(10);
            Execute("DELETE FROM invitations WHERE expires<=$now OR device_id=$id", ("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds()), ("$id", id));
            Execute("INSERT INTO invitations(token_hash,device_id,expires,fingerprint) VALUES($hash,$id,$expires,$fp)", ("$hash", Hash(secret)), ("$id", id), ("$expires", expiry.ToUnixTimeSeconds()), ("$fp", fingerprint));
            return new("", secret, fingerprint, expiry);
        }
    }
    public PairResult? Pair(PairRequest request) {
        if (request.Version != RelayProtocol.Version || request.Code?.Length is not (>= 32 and <= 128) || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100) return null;
        lock (gate) {
            // Immediate transaction also serializes competing invitations across CLI/service processes.
            using var transaction = database.BeginTransaction(deferred: false);
            string device, fingerprint;
            using (var command = Command("SELECT i.device_id,i.fingerprint FROM invitations i JOIN devices d ON i.device_id=d.id WHERE i.token_hash=$hash AND i.expires>$now AND d.revoked=0 AND i.fingerprint=d.fingerprint AND (SELECT COUNT(*) FROM clients c WHERE c.device_id=d.id AND c.revoked=0)<32", ("$hash", Hash(request.Code)), ("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds()))) {
                using var reader = command.ExecuteReader(); if (!reader.Read()) return null;
                device = reader.GetString(0); fingerprint = reader.GetString(1);
            }
            var client = Guid.NewGuid().ToString("N"); var secret = Secret();
            Execute("DELETE FROM invitations WHERE token_hash=$hash", ("$hash", Hash(request.Code)));
            Execute("INSERT INTO clients(id,device_id,name,token_hash) VALUES($id,$device,$name,$hash)", ("$id", client), ("$device", device), ("$name", request.Name), ("$hash", Hash(secret)));
            transaction.Commit(); return new(device, client, secret, fingerprint);
        }
    }
    public bool ActiveDevice(string id) { lock (gate) { using var c = Command("SELECT COUNT(*) FROM devices WHERE id=$id AND revoked=0", ("$id", id)); return (long)c.ExecuteScalar()! == 1; } }
    public bool ActiveClient(string id) { lock (gate) { using var c = Command("SELECT COUNT(*) FROM clients c JOIN devices d ON c.device_id=d.id WHERE c.id=$id AND c.revoked=0 AND d.revoked=0", ("$id", id)); return (long)c.ExecuteScalar()! == 1; } }
    public void Revoke(string kind, string id) {
        lock (gate) { if (kind is not ("device" or "client")) throw new ArgumentException("Unknown credential type.");
            Execute("UPDATE " + (kind == "device" ? "devices" : "clients") + " SET revoked=1 WHERE id=$id", ("$id", id)); }
    }
    public string[] List(string kind) {
        lock (gate) {
            using var command = Command(kind == "devices" ? "SELECT id,name,revoked FROM devices ORDER BY name" : "SELECT id,name || ' device=' || device_id,revoked FROM clients ORDER BY name");
            using var reader = command.ExecuteReader(); var rows = new List<string>();
            while (reader.Read()) rows.Add($"{reader.GetString(0)}  {reader.GetString(1)}  {(reader.GetInt32(2) != 0 ? "revoked" : "active")}");
            return rows.ToArray();
        }
    }
    public static void ValidateName(string? name) {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Any(char.IsControl))
            throw new ArgumentException("Device name must contain 1–100 printable characters.");
    }
    public bool RenameDevice(string id, string name) {
        ValidateName(name);
        lock (gate) return Execute("UPDATE devices SET name=$name WHERE id=$id AND revoked=0", ("$name", name.Trim()), ("$id", id)) == 1;
    }
    public (ManagedDevice[] Devices, ManagedClient[] Clients) ManagementSnapshot() {
        lock (gate) {
            var devices = new List<ManagedDevice>(); var clients = new List<ManagedClient>();
            using (var command = Command("SELECT id,name,fingerprint,revoked FROM devices ORDER BY rowid DESC")) {
                using var reader = command.ExecuteReader();
                while (reader.Read()) devices.Add(new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3) != 0));
            }
            using (var command = Command("SELECT c.id,c.device_id,c.name,(c.revoked OR d.revoked) FROM clients c JOIN devices d ON d.id=c.device_id ORDER BY c.rowid DESC")) {
                using var reader = command.ExecuteReader();
                while (reader.Read()) clients.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3) != 0));
            }
            return (devices.ToArray(), clients.ToArray());
        }
    }
    public void Dispose() => database.Dispose();
}
