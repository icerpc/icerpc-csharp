// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    internal sealed class SslConnection : SingleStreamConnection
    {
        public override IConnectionInformation ConnectionInformation => _underlying.ConnectionInformation;

        internal SslStream? SslStream { get; private set; }

        /// <inheritdoc/>
        internal override Socket? NetworkSocket => _underlying.NetworkSocket;

        private BufferedStream? _writeStream;
        private readonly SingleStreamConnection _underlying;
        private readonly Socket _socket;

        public override async ValueTask<(SingleStreamConnection, Endpoint?)> AcceptAsync(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            if (authenticationOptions == null)
            {
                throw new InvalidOperationException("cannot accept TLS connection: no TLS authentication configured");
            }
            await AuthenticateAsync(sslStream =>
                sslStream.AuthenticateAsServerAsync(authenticationOptions, cancel)).ConfigureAwait(false);
            return (this, endpoint);
        }

        public override async ValueTask<(SingleStreamConnection, Endpoint)> ConnectAsync(
            Endpoint endpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            await AuthenticateAsync(sslStream =>
                sslStream.AuthenticateAsClientAsync(authenticationOptions!, cancel)).ConfigureAwait(false);
            return (this, endpoint);
        }

        public override ValueTask CloseAsync(long errorCode, CancellationToken cancel) => default;

        public override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (buffer.Length == 0)
            {
                throw new ArgumentException($"empty {nameof(buffer)}");
            }

            int received;
            try
            {
                received = await SslStream!.ReadAsync(buffer, cancel).ConfigureAwait(false);
            }
            catch (IOException ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException(ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new TransportException(ex);
            }
            if (received == 0)
            {
                throw new ConnectionLostException();
            }
            return received;
        }

        public override ValueTask<ArraySegment<byte>> ReceiveDatagramAsync(CancellationToken cancel) =>
            _underlying.ReceiveDatagramAsync(cancel);

        public override async ValueTask<int> SendAsync(IList<ArraySegment<byte>> buffer, CancellationToken cancel)
        {
            try
            {
                int sent = 0;
                foreach (ArraySegment<byte> segment in buffer)
                {
                    await _writeStream!.WriteAsync(segment, cancel).ConfigureAwait(false);
                    sent += segment.Count;
                }
                await _writeStream!.FlushAsync(cancel).ConfigureAwait(false);
                return sent;
            }
            catch (IOException ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new TransportException(ex);
            }
        }

        public override ValueTask<int> SendDatagramAsync(IList<ArraySegment<byte>> buffer, CancellationToken cancel) =>
            _underlying.SendDatagramAsync(buffer, cancel);

        public override string ToString() => _underlying.ToString()!;

        protected override void Dispose(bool disposing)
        {
            _underlying.Dispose();

            SslStream!.Dispose();

            try
            {
                _writeStream?.Dispose();
            }
            catch
            {
                // Ignore: the buffer flush which will fail since the underlying transport is closed.
            }
        }

        internal SslConnection(SingleStreamConnection underlying, Socket socket)
            : base(underlying.Logger)
        {
            _socket = socket;
            _underlying = underlying;
        }

        private async Task AuthenticateAsync(Func<SslStream, Task> authenticate)
        {
            // This can only be created with a connected socket.
            SslStream = new SslStream(new NetworkStream(_socket, false), false);
            try
            {
                await authenticate(SslStream).ConfigureAwait(false);
            }
            catch (IOException ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException(ex);
            }
            catch (IOException ex)
            {
                throw new TransportException(ex);
            }
            catch (AuthenticationException ex)
            {
                Logger.LogTlsAuthenticationFailed(ex);
                throw new TransportException(ex);
            }

            Logger.LogTlsAuthenticationSucceeded(SslStream);

            // Use a buffered stream for writes. This ensures that small requests which are composed of multiple
            // small buffers will be sent within a single SSL frame.
            _writeStream = new BufferedStream(SslStream);
        }
    }
}
