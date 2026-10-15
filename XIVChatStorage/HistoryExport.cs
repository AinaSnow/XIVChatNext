using System;
using System.IO;
using System.Text;
using XIVChatCommon.Message.Server;

namespace XIVChatStorage;

public enum ExportFormat { Text, Rtf, Markdown }

public static class HistoryExport {
    public static ExportFormat FormatForExtension(string extension) => extension.ToLowerInvariant() switch {
        ".rtf" => ExportFormat.Rtf,
        ".md" or ".markdown" => ExportFormat.Markdown,
        _ => ExportFormat.Text,
    };

    public static string Line(ServerMessage message, bool timestamps) =>
        (timestamps ? message.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " " : "") +
        "[" + message.Channel + (string.IsNullOrWhiteSpace(message.ChannelName) ? "" : " · " + message.ChannelName) + "] " +
        (string.IsNullOrEmpty(message.SenderText) ? "" : message.SenderText + ": ") + message.ContentText;

    public static string EscapeRtf(string text) {
        var result = new StringBuilder();
        foreach (var ch in text) {
            switch (ch) {
                case '\\': case '{': case '}': result.Append('\\').Append(ch); break;
                case '\r': break;
                case '\n': result.Append("\\line "); break;
                case '\t': result.Append("\\tab "); break;
                default:
                    if (ch >= 32 && ch <= 126) result.Append(ch);
                    else result.Append("\\u").Append((short)ch).Append('?');
                    break;
            }
        }
        return result.ToString();
    }

    /// <summary>Escapes chat text so Markdown shows it literally: no accidental emphasis, code, links, HTML, tables or strikethrough.</summary>
    /// <remarks>Each exported line starts with "- " and then a timestamp or "[", so block syntax such as headings cannot start it.</remarks>
    public static string EscapeMarkdown(string text) {
        var result = new StringBuilder(text.Length + 8);
        foreach (var ch in text) {
            switch (ch) {
                case '\\': case '`': case '*': case '_': case '[': case ']': case '<': case '>': case '|': case '~':
                    result.Append('\\').Append(ch); break;
                case '\r': break;
                // A hard line break that stays inside the list item.
                case '\n': result.Append("\\\n  "); break;
                default: result.Append(ch); break;
            }
        }
        return result.ToString();
    }

    /// <summary>Writes the document start: nothing for text, the RTF header, or a Markdown title.</summary>
    public static void WriteHeader(TextWriter writer, ExportFormat format, string? title, DateTime exportedUtc) {
        switch (format) {
            case ExportFormat.Rtf:
                writer.Write("{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Segoe UI;}}\\uc1\\f0 ");
                break;
            case ExportFormat.Markdown:
                writer.Write("# " + EscapeMarkdown(string.IsNullOrWhiteSpace(title) ? "XIVChat Next" : title) + "\n\n");
                writer.Write("XIVChat Next · " + exportedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "\n");
                break;
        }
    }

    /// <summary>Writes one message in the same visible form as the text export.</summary>
    /// <param name="previousDay">Markdown groups messages under a heading per local day; tracks the day of the last message written.</param>
    public static void WriteLine(TextWriter writer, ExportFormat format, string line, DateTime timestampUtc, bool timestamps, ref DateOnly? previousDay) {
        switch (format) {
            case ExportFormat.Rtf:
                writer.Write(EscapeRtf(line) + "\\par\n");
                break;
            case ExportFormat.Markdown:
                var day = DateOnly.FromDateTime(timestampUtc.ToLocalTime());
                if (timestamps && day != previousDay) {
                    writer.Write("\n## " + day.ToString("yyyy-MM-dd") + "\n\n");
                    previousDay = day;
                } else if (previousDay == null) {
                    writer.Write("\n");
                    previousDay = day;
                }
                writer.Write("- " + EscapeMarkdown(line) + "\n");
                break;
            default:
                writer.Write(line + Environment.NewLine);
                break;
        }
    }

    public static void WriteFooter(TextWriter writer, ExportFormat format) {
        if (format == ExportFormat.Rtf) writer.Write("}");
    }
}
