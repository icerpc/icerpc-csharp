// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    internal class TcpConnection : SingleStreamConnection
    {
        /// <inheritdoc/>
        public override ConnectionInformation ConnectionInformation =>
            _connectionInformation ??= new TcpConnectionInformation(_socket, sslStream: null);

        /// <inheritdoc/>
        internal override Socket? NetworkSocket => _socket;
        private readonly EndPoint? _addr;
        private TcpConnectionInformation? _connectionInformation;
        private readonly Socket _socket;

        // See https://tools.ietf.org/html/rfc5246#appendix-A.4
        private const byte TlsHandshakeRecord = 0x16;

        public override async ValueTask<(SingleStreamConnection, Endpoint?)> AcceptAsync(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            try
            {
                // TODO: rework this code using endpoint.IsSecure

                // On the server side, when accepting a new connection for Ice2 endpoint, the TCP socket checks
                // the first byte sent by the peer to figure out if the peer tries to establish a TLS connection.
                bool secure = false;
                if (endpoint.Protocol == Protocol.Ice2)
                {
                    // Peek one byte into the tcp stream to see if it contains the TLS handshake record
                    var buffer = new ArraySegment<byte>(new byte[1]);
                    int received = await _socket.ReceiveAsync(buffer, SocketFlags.Peek, cancel).ConfigureAwait(false);
                    if (received == 0)
                    {
                        throw new ConnectionLostException();
                    }
                    Debug.Assert(received == 1);
                    secure = buffer.Array![0] == TlsHandshakeRecord;
                }

                // If a secure connection is needed, a new SslConnection is created and returned from this method.
                // The caller is responsible for using the returned SslConnection in place of this TcpConnection.
                if (endpoint.IsSecure ?? secure)
                {
                    var sslConnection = new SslConnection(this, _socket);
                    await sslConnection.AcceptAsync(endpoint, authenticationOptions, cancel).ConfigureAwait(false);
                    return (sslConnection, ((TcpEndpoint)endpoint).Clone(_socket.RemoteEndPoint!));
                }
                else
                {
                    return (this, ((TcpEndpoint)endpoint).Clone(_socket.RemoteEndPoint!));
                }
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

        public override async ValueTask<(SingleStreamConnection, Endpoint)> ConnectAsync(
            Endpoint endpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Debug.Assert(_addr != null);

            try
            {
                // Connect to the peer.
                await _socket.ConnectAsync(_addr, cancel).ConfigureAwait(false);

                // If a secure socket is requested, create an SslConnection and return it from this method. The caller
                // is responsible for using the returned SslConnection instead of using this TcpConnection.
                if (authenticationOptions != null)
                {
                    var sslConnection = new SslConnection(this, _socket);
                    await sslConnection.ConnectAsync(endpoint, authenticationOptions, cancel).ConfigureAwait(false);
                    return (sslConnection, ((TcpEndpoint)endpoint).Clone(_socket.LocalEndPoint!));
                }
                else
                {
                    return (this, ((TcpEndpoint)endpoint).Clone(_socket.LocalEndPoint!));
                }
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
                received = await _socket.ReceiveAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
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

        public override ValueTask<ArraySegment<byte>> ReceiveDatagramAsync(CancellationToken cancel) =>
            throw new InvalidOperationException("TCP doesn't support datagrams");

        public override async ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
        {
            try
            {
                return await _socket.SendAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
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

        public override ValueTask<int> SendDatagramAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel) =>
            throw new InvalidOperationException("TCP doesn't support datagrams");

        protected override void Dispose(bool disposing) => _socket.Dispose();

        internal TcpConnection(Socket fd, ILogger logger, EndPoint? addr = null)
            : base(logger)
        {
            _addr = addr;

            // The socket is not connected if a client socket, it's connected otherwise.
            _socket = fd;
        }
    }
}
