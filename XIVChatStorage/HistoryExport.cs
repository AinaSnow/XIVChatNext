using System.Text;
using XIVChatCommon.Message.Server;

namespace XIVChatStorage;

public static class HistoryExport {
    public static string Line(ServerMessage message, bool timestamps) =>
        (timestamps ? message.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " " : "") +
        "[" + message.Channel + "] " + (string.IsNullOrEmpty(message.SenderText) ? "" : message.SenderText + ": ") + message.ContentText;
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
}
