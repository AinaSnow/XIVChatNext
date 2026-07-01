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

        internal Channel<Encodable> Queue { get; } = Channel.CreateUnbounded<Encodable>();

        internal uint BacklogSequence { get; set; }

        internal void Disconnect() {
            this.Connected = false;
            this.TokenSource.Cancel();

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

        private ChannelWriter<IToRelay> ToRelay { get; }
        private Channel<byte[]> FromRelay { get; }

        internal ChannelWriter<byte[]> FromRelayWriter => this.FromRelay.Writer;

        private List<byte> ReadBuffer { get; } = [];
        private List<byte> WriteBuffer { get; } = [];

        internal RelayConnected(byte[] publicKey, IPAddress? remote, ChannelWriter<IToRelay> toRelay, Channel<byte[]> fromRelay) {
            this.PublicKey = publicKey;
            this.Remote = remote;
            this.Connected = true;
            this.ToRelay = toRelay;
            this.FromRelay = fromRelay;
        }

        public override void Flush() {
            if (this.WriteBuffer.Count == 0) {
                return;
            }

            var message = new RelayedMessage {
                PublicKey = this.PublicKey.ToList(),
                Message = this.WriteBuffer.ToList(),
            };
            this.WriteBuffer.Clear();

            // write the contents of the write buffer to the relay
            this.ToRelay.WriteAsync(message).AsTask().Wait();
        }

        public override long Seek(long offset, SeekOrigin origin) {
            throw new NotSupportedException();
        }

        public override void SetLength(long value) {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) {
            var read = 0;

            // if there are bytes in the buffer, take from them first
            if (this.ReadBuffer.Count > 0) {
                // determine how many bytes to take from the buffer
                var toRead = count > this.ReadBuffer.Count ? this.ReadBuffer.Count : count;

                // copy bytes, then remove them
                this.ReadBuffer.CopyTo(0, buffer, offset, toRead);
                this.ReadBuffer.RemoveRange(0, toRead);
                // increment the read count
                read += toRead;
            }

            // if we've read everything, return
            if (read == count) {
                return read;
            }

            // get new bytes
            var readTask = this.FromRelay.Reader.ReadAsync().AsTask();
            readTask.Wait();
            var bytes = readTask.Result;

            // add new bytes to buffer
            this.ReadBuffer.AddRange(bytes);

            // and keep going
            return read + this.Read(buffer, offset + read, count - read);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            var read = 0;

            // if there are bytes in the buffer, take from them first
            if (this.ReadBuffer.Count > 0) {
                // determine how many bytes to take from the buffer
                var toRead = count > this.ReadBuffer.Count ? this.ReadBuffer.Count : count;

                // copy bytes, then remove them
                this.ReadBuffer.CopyTo(0, buffer, offset, toRead);
                this.ReadBuffer.RemoveRange(0, toRead);
                // increment the read count
                read += toRead;
            }

            // if we've read everything, return
            if (read == count) {
                return read;
            }

            // get new bytes
            var bytes = await this.FromRelay.Reader.ReadAsync(cancellationToken);

            // add new bytes to buffer
            this.ReadBuffer.AddRange(bytes);

            // and keep going
            return read + await this.ReadAsync(buffer, offset + read, count - read, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count) {
            // create a new array of the bytes to send
            var bytes = new byte[count];
            // copy bytes over
            Array.Copy(buffer, 0, bytes, 0, count);
            // push them into the write buffer
            this.WriteBuffer.AddRange(bytes);
        }

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();

        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
