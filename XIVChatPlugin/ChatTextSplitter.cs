using System;
using System.Collections.Generic;
using System.Text;

namespace XIVChatPlugin {
    internal static class ChatTextSplitter {
        // The game's limit is UTF-8 bytes, including any repeated chat command / tell target.
        internal static string[] Split(string text, int maxBytes = 500) {
            if (text.IndexOf('\0') >= 0) throw new ArgumentException("Embedded NUL in chat input.");
            if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return new[] { text };
            var prefix = "";
            if (text.StartsWith('/')) {
                var end = text.IndexOf(' ');
                if (end < 0) throw new ArgumentException("Chat command is too long.");
                var command = text[..end];
                if (command.Equals("/t", StringComparison.OrdinalIgnoreCase) || command.Equals("/tell", StringComparison.OrdinalIgnoreCase)) {
                    // Game character names have two words. Preserve the full Name@World target on every part.
                    end = text.IndexOf(' ', end + 1);
                    if (end >= 0) end = text.IndexOf(' ', end + 1);
                    if (end < 0) throw new ArgumentException("Tell target is incomplete.");
                }
                prefix = text[..(end + 1)]; text = text[(end + 1)..];
            }
            var budget = maxBytes - Encoding.UTF8.GetByteCount(prefix);
            if (budget < 4) throw new ArgumentException("Chat prefix leaves no room for text.");
            var parts = new List<string>();
            var builder = new StringBuilder(prefix);
            var used = 0;
            foreach (var rune in text.EnumerateRunes()) {
                if (used + rune.Utf8SequenceLength > budget) {
                    parts.Add(builder.ToString()); builder.Clear().Append(prefix); used = 0;
                }
                builder.Append(rune.ToString()); used += rune.Utf8SequenceLength;
            }
            if (used > 0) parts.Add(builder.ToString());
            return parts.ToArray();
        }
    }
}
