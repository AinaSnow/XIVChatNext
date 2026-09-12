using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using XIVChatCommon;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Client;
using XIVChatCommon.Message.Relay;

namespace XIVChatPlugin {
    internal abstract class BaseClient : Stream {
        internal virtual bool Connected { get; set; }

        internal HandshakeInfo? Handshake { get; set; }

        internal ClientPreferences? Preferences { get; set; }

        internal IPAddress? Remote { get; set; }

        internal CancellationTokenSource TokenSource { get; } = new();

        internal BoundedByteQueue Queue { get; } = new(512, 8 * 1024 * 1024);
        internal bool Ready { get; set; }
        internal ChannelSubscription Subscription { get; set; } = ChannelSubscription.All;
        internal Action<string>? OnFailure { get; set; }
        private int disconnected;
        internal string? DisconnectReason { get; private set; }

        internal bool Send(Encodable message) => this.SendEncoded(message.Encode());
        internal bool TrySendScreenshot(byte[] bytes) => !this.TokenSource.IsCancellationRequested &&
            bytes.Length <= ScreenshotProtocol.MaxPacketBytes && this.Queue.TryWriteWithin(bytes, 4, 2 * ScreenshotProtocol.MaxPacketBytes);
        internal bool SendEncoded(byte[] bytes) {
            if (this.TokenSource.IsCancellationRequested) return false;
            if (bytes.Length + SecretMessage.MacSize <= 128_000 && this.Queue.TryWrite(bytes)) return true;
            this.Disconnect("Outgoing queue exceeded 512 packets / 8 MiB, or one packet exceeded the frame limit.");
            return false;
        }

        internal uint BacklogSequence { get; set; }

        internal void Disconnect(string? reason = null) {
            if (Interlocked.Exchange(ref this.disconnected, 1) != 0) return;
            this.DisconnectReason = reason;
            this.Ready = false;
            this.Connected = false;
            this.Queue.Close();
            this.TokenSource.Cancel();
            if (reason != null) this.OnFailure?.Invoke(reason);

            try {
                this.Close();
            } catch (ObjectDisposedException) {
                // ignored
            }
        }

        internal T? GetPreference<T>(ClientPreference pref, T? def = default) {
            var prefs = this.Preferences;

            if (prefs == null) {
                return def;
            }

            return prefs.TryGetValue(pref, out T result) ? result : def;
        }
    }

    internal sealed class TcpConnected : BaseClient {
        private TcpClient Client { get; }
        private readonly Stream _streamImplementation;
        private bool _connected;

        internal override bool Connected {
            get {
                var ret = this._connected;
                try {
                    ret = ret && this.Client.Connected;
                } catch (ObjectDisposedException) {
                    return false;
                }

                return ret;
            }
            set => this._connected = value;
        }

        internal TcpConnected(TcpClient client) {
            this.Client = client;

            this.Client.ReceiveTimeout = 5_000;
            this.Client.SendTimeout = 5_000;

            this.Client.Client.ReceiveTimeout = 5_000;
            this.Client.Client.SendTimeout = 5_000;

            if (this.Client.Client.RemoteEndPoint is IPEndPoint endPoint) {
                this.Remote = endPoint.Address;
            }

            this.Connected = this.Client.Connected;
            this._streamImplementation = this.Client.GetStream();
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                this._streamImplementation.Dispose();
                this.Client.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void Flush() {
            this._streamImplementation.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin) {
            return this._streamImplementation.Seek(offset, origin);
        }

        public override void SetLength(long value) {
            this._streamImplementation.SetLength(value);
        }

        public override int Read(byte[] buffer, int offset, int count) {
            return this._streamImplementation.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            return this._streamImplementation.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count) {
            this._streamImplementation.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            return this._streamImplementation.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override bool CanRead => this._streamImplementation.CanRead;

        public override bool CanSeek => this._streamImplementation.CanSeek;

        public override bool CanWrite => this._streamImplementation.CanWrite;

        public override long Length => this._streamImplementation.Length;

        public override long Position {
            get => this._streamImplementation.Position;
            set => this._streamImplementation.Position = value;
        }
    }

    internal sealed class RelayConnected : BaseClient {
        internal byte[] PublicKey { get; }
        private readonly Func<IToRelay, CancellationToken, Task> send;
        private readonly BoundedByteQueue incoming = new(128, 2 * 1024 * 1024);
        private readonly MemoryStream writeBuffer = new();
        private BoundedByteQueue.Lease? readPacket;
        private int readOffset;

        internal RelayConnected(byte[] publicKey, IPAddress? remote, Func<IToRelay, CancellationToken, Task> send) {
            this.PublicKey = publicKey; this.Remote = remote; this.send = send; this.Connected = true;
        }
        internal void Receive(byte[] bytes) {
            if (bytes.Length > 128_028 || !this.incoming.TryWrite(bytes)) this.Disconnect("Relay input exceeded its packet / byte budget.");
        }
        public override void Flush() => this.FlushAsync(this.TokenSource.Token).GetAwaiter().GetResult();
        public override async Task FlushAsync(CancellationToken cancellationToken) {
            if (this.writeBuffer.Length == 0) return;
            var bytes = this.writeBuffer.ToArray(); this.writeBuffer.SetLength(0);
            // Keep the base client's outgoing lease reserved until the websocket has sent this packet.
            await this.send(new RelayedMessage { PublicKey = this.PublicKey.ToList(), Message = bytes.ToList() }, cancellationToken);
        }
        public override int Read(byte[] buffer, int offset, int count) =>
            this.ReadAsync(buffer, offset, count, this.TokenSource.Token).GetAwaiter().GetResult();
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            if (count == 0) return 0;
            while (this.readPacket == null) {
                this.readPacket = await this.incoming.ReadAsync(cancellationToken);
                this.readOffset = 0;
                if (this.readPacket.Bytes.Length == 0) { this.readPacket.Dispose(); this.readPacket = null; }
            }
            var packet = this.readPacket;
            var take = Math.Min(count, packet.Bytes.Length - this.readOffset);
            Array.Copy(packet.Bytes, this.readOffset, buffer, offset, take); this.readOffset += take;
            if (this.readOffset == packet.Bytes.Length) { this.readPacket = null; packet.Dispose(); }
            return take;
        }
        public override void Write(byte[] buffer, int offset, int count) {
            this.TokenSource.Token.ThrowIfCancellationRequested();
            if (count > 128_028 - this.writeBuffer.Length) throw new IOException("Relay frame exceeds the frame budget.");
            this.writeBuffer.Write(buffer, offset, count);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested(); this.Write(buffer, offset, count); return Task.CompletedTask;
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { this.incoming.Close(); this.readPacket?.Dispose(); this.writeBuffer.Dispose(); }
            base.Dispose(disposing);
        }
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
