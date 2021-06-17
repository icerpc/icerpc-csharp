// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    internal class TcpConnection : SingleStreamConnection
    {
        /// <inheritdoc/>
        public override ConnectionInformation ConnectionInformation =>
            _connectionInformation ??= new TcpConnectionInformation(_socket, SslStream);

        /// <inheritdoc/>
        internal override Socket? NetworkSocket => _socket;

        internal SslStream? SslStream { get; private set; }

        private readonly EndPoint? _addr;
        private TcpConnectionInformation? _connectionInformation;
        private readonly Socket _socket;
        private BufferedStream? _writeStream;

        // See https://tools.ietf.org/html/rfc5246#appendix-A.4
        private const byte TlsHandshakeRecord = 0x16;

        public override async ValueTask<Endpoint?> AcceptAsync(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            try
            {
                // On the server side, when accepting a new connection for Ice2 endpoint, the TCP socket checks
                // the first byte sent by the peer to figure out if the peer tries to establish a TLS connection.
                bool secure = false;
                if (endpoint.Protocol == Protocol.Ice2)
                {
                    // Peek one byte into the tcp stream to see if it contains the TLS handshake record
                    Memory<byte> buffer = new byte[1];
                    int received = await _socket.ReceiveAsync(buffer, SocketFlags.Peek, cancel).ConfigureAwait(false);
                    if (received == 0)
                    {
                        throw new ConnectionLostException();
                    }
                    Debug.Assert(received == 1);
                    secure = buffer.Span[0] == TlsHandshakeRecord;
                }

                // If a secure connection is needed, a new SslConnection is created and returned from this method.
                // The caller is responsible for using the returned SslConnection in place of this TcpConnection.
                if (endpoint.IsSecure ?? secure)
                {
                    if (authenticationOptions == null)
                    {
                        throw new InvalidOperationException(
                            "cannot accept TLS connection: no TLS authentication configured");
                    }
                    await AuthenticateAsync(sslStream =>
                        sslStream.AuthenticateAsServerAsync(authenticationOptions, cancel)).ConfigureAwait(false);
                }
                return ((TcpEndpoint)endpoint).Clone(_socket.RemoteEndPoint!);
            }
            catch (Exception ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException(ex);
            }
            catch (Exception ex) when (cancel.IsCancellationRequested)
            {
                throw new OperationCanceledException(null, ex, cancel);
            }
            catch (Exception ex)
            {
                throw new TransportException(ex);
            }
        }

        // We can't shutdown the socket write side because it might block.
        public override ValueTask CloseAsync(long errorCode, CancellationToken cancel) => default;

        public override async ValueTask<Endpoint> ConnectAsync(
            Endpoint endpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Debug.Assert(_addr != null);

            try
            {
                // Connect to the peer.
                await _socket.ConnectAsync(_addr, cancel).ConfigureAwait(false);

                if (authenticationOptions != null)
                {
                    await AuthenticateAsync(sslStream =>
                        sslStream.AuthenticateAsClientAsync(authenticationOptions, cancel)).ConfigureAwait(false);
                }
                return ((TcpEndpoint)endpoint).Clone(_socket.LocalEndPoint!);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                throw new ConnectionRefusedException(ex);
            }
            catch (SocketException ex)
            {
                throw new ConnectFailedException(ex);
            }
            catch (Exception ex) when (cancel.IsCancellationRequested)
            {
                throw new OperationCanceledException(null, ex, cancel);
            }
            catch (Exception ex)
            {
                throw new TransportException(ex);
            }
        }

        public override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (buffer.Length == 0)
            {
                throw new ArgumentException($"empty {nameof(buffer)}");
            }

            int received;
            try
            {
                if (SslStream is SslStream sslStream)
                {
                    received = await sslStream.ReadAsync(buffer, cancel).ConfigureAwait(false);
                }
                else
                {
                    received = await _socket.ReceiveAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
                }
            }
            catch (IOException ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException(ex);
            }
            catch (SocketException ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException(ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (cancel.IsCancellationRequested)
            {
                throw new OperationCanceledException(null, ex, cancel);
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

        public override async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
        {
            try
            {
                if (_writeStream is BufferedStream writeStream)
                {
                    await _writeStream.WriteAsync(buffer, cancel).ConfigureAwait(false);
                    await _writeStream.FlushAsync(cancel).ConfigureAwait(false);
                }
                else
                {
                    await _socket.SendAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
                }
            }
            catch (IOException ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException();
            }
            catch (SocketException ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException(ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (cancel.IsCancellationRequested)
            {
                throw new OperationCanceledException(null, ex, cancel);
            }
            catch (Exception ex)
            {
                throw new TransportException(ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            _socket.Dispose();
            SslStream?.Dispose();

            try
            {
                _writeStream?.Dispose();
            }
            catch
            {
                // Ignore: the buffer flush which will fail since the underlying transport is closed.
            }
        }

        internal TcpConnection(Socket fd, ILogger logger, EndPoint? addr = null)
            : base(logger)
        {
            _addr = addr;

            // The socket is not connected if a client socket, it's connected otherwise.
            _socket = fd;
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
