using System;
using System.Collections.Generic;
using System.Linq;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Server;

namespace XIVChatCommon.Message {
    public static class HistoryPager {
        // Includes MessagePack envelope and SecretBox overhead below the 128,000 byte frame limit.
        private const int Budget = 120_000;
        public static ServerHistory Create(IEnumerable<ServerMessage> history, string service, string run, long latest, ClientHistory request) {
            var sameRun = request.After?.ServiceId == service && request.After?.RunId == run;
            var after = sameRun ? Math.Max(0, request.After!.Sequence) : 0;
            var through = sameRun && request.Through.HasValue ? Math.Clamp(request.Through.Value, 0, latest) : latest;
            var available = history.Where(m => m.Sequence > after && m.Sequence <= through).ToArray();
            var page = new ServerHistory {
                Through = through,
                HasGap = (request.After != null && !sameRun) || after > latest ||
                    (after < through && (available.Length == 0 || available[0].Sequence > after + 1)),
                Cursor = new HistoryCursor { ServiceId = service, RunId = run, Sequence = after },
            };
            var messages = new List<ServerMessage>();
            var size = 1024;
            foreach (var message in available) {
                if (messages.Count == 200) break;
                var messageSize = message.Encode().Length;
                if (size + messageSize > Budget) {
                    if (messages.Count == 0) { page.HasGap = true; page.Cursor.Sequence = message.Sequence; continue; }
                    break;
                }
                if (message.Sequence > page.Cursor.Sequence + 1) page.HasGap = true;
                messages.Add(message);
                size += messageSize;
                page.Cursor.Sequence = message.Sequence;
            }
            page.Messages = messages.ToArray();
            page.HasMore = available.Any(m => m.Sequence > page.Cursor.Sequence);
            if (!page.HasMore) {
                if (page.Cursor.Sequence < through) page.HasGap = true;
                page.Cursor.Sequence = through;
            }
            return page;
        }
    }
}
