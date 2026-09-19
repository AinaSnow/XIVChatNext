using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using XIVChatCommon.Message;

namespace XIVChatCommon.Presentation;

// Offsets include one placeholder per IconChunk, matching IdentityDisplay.ChunkText.
internal static class LinkedPlayerText {
    internal sealed record Link(int Start, int Length, CharacterIdentity Player);
    internal static IReadOnlyList<Link> Read(byte[] bytes, Func<ushort, string?>? worldName) {
        var links = new List<Link>();
        using var reader = new BinaryReader(new MemoryStream(bytes));
        var plain = new List<byte>(); int offset = 0, start = 0;
        CharacterIdentity? player = null;
        void Flush() { offset += Encoding.UTF8.GetCharCount(plain.ToArray()); plain.Clear(); }
        try {
            while (reader.BaseStream.Position < reader.BaseStream.Length) {
                var b = reader.ReadByte();
                if (b != 2) { plain.Add(b); continue; }
                Flush();
                var type = reader.ReadByte(); var length = checked((int)XivString.GetInteger(reader));
                var payload = reader.ReadBytes(length);
                if (payload.Length != length || reader.ReadByte() != 3) break;
                using var data = new BinaryReader(new MemoryStream(payload));
                if (type == 0x12) { offset++; continue; }
                if (type != 0x27 || payload.Length == 0) continue;
                var subtype = data.ReadByte();
                if (subtype == 1) {
                    player = null;
                    data.ReadByte(); var world = checked((ushort)XivString.GetInteger(data)); data.ReadBytes(2);
                    var nameLength = checked((int)XivString.GetInteger(data));
                    var nameBytes = data.ReadBytes(nameLength);
                    if (nameBytes.Length != nameLength) continue;
                    var name = Encoding.UTF8.GetString(nameBytes);
                    if (string.IsNullOrWhiteSpace(name) || name.Length > 64) continue;
                    player = new() { Name = name, HomeWorldId = world, HomeWorld = worldName?.Invoke(world) ?? "" }; start = offset;
                } else if (subtype is 0xCF or 2) {
                    if (player != null && offset - start == player.Name.Length) links.Add(new(start, offset - start, player));
                    player = null;
                }
            }
        } catch (Exception ex) when (ex is IOException or ArgumentException or OverflowException) { /* Keep only fully terminated links. */ }
        return links;
    }
}
