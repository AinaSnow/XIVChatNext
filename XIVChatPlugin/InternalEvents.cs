using System;

namespace XIVChatPlugin {
    internal class InternalEvents {
        internal delegate void NewClientDelegate(Guid id, BaseClient client);

        internal event NewClientDelegate? NewClient;

        internal void FireNewClientEvent(Guid id, BaseClient client) {
            this.NewClient?.Invoke(id, client);
        }
    }
}
