using System;
using System.Collections.Generic;
using System.Linq;
using XIVChatCommon;
using XIVChatCommon.Message;

namespace XIVChatPlugin;

// Rendering/enrichment failures must never prevent a raw message entering history.
internal static class ChatChunkFallback {
    internal static IReadOnlyList<Chunk> Convert(Func<IEnumerable<Chunk>> render, byte[] encoded, uint? colour, Action<Exception> report) {
        try { return render().ToArray(); }
        catch (Exception ex) {
            report(ex);
            try {
                var chunks = XivString.ToChunks(encoded);
                foreach (var text in chunks.OfType<TextChunk>()) text.FallbackColour = colour;
                return chunks;
            } catch (Exception) {
                return new Chunk[] { new TextChunk("[Message could not be rendered]") { FallbackColour = colour } };
            }
        }
    }
}
