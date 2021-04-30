// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    internal class TcpSocket : SingleStreamSocket, ITcpSocket
    {
        /// <inheritdoc/>
        public bool CheckCertRevocationStatus => _sslStream?.CheckCertRevocationStatus ?? false;

        /// <inheritdoc/>
        public bool IsEncrypted => _sslStream?.IsEncrypted ?? false;

        /// <inheritdoc/>
        public bool IsMutuallyAuthenticated => _sslStream?.IsMutuallyAuthenticated ?? false;

        /// <inheritdoc/>
        public bool IsSecure => _sslStream != null;

        /// <inheritdoc/>
        public bool IsSigned => _sslStream?.IsSigned ?? false;

        /// <inheritdoc/>
        public X509Certificate? LocalCertificate => _sslStream?.LocalCertificate;

        /// <inheritdoc/>
        public IPEndPoint? LocalEndPoint
        {
            get
            {
                try
                {
                    return _socket.LocalEndPoint as IPEndPoint;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <inheritdoc/>
        public SslApplicationProtocol? NegotiatedApplicationProtocol => _sslStream?.NegotiatedApplicationProtocol;

        /// <inheritdoc/>
        public TlsCipherSuite? NegotiatedCipherSuite => _sslStream?.NegotiatedCipherSuite;

        /// <inheritdoc/>
        public X509Certificate? RemoteCertificate => _sslStream?.RemoteCertificate;

        /// <inheritdoc/>
        public IPEndPoint? RemoteEndPoint
        {
            get
            {
                try
                {
                    return _socket.RemoteEndPoint as IPEndPoint;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <inheritdoc/>
        public override ISocket Socket => this;

        /// <inheritdoc/>
        public SslProtocols? SslProtocol => _sslStream?.SslProtocol;

        /// <inheritdoc/>
        internal override Socket? NetworkSocket => _socket;

        private readonly EndPoint? _addr;
        private readonly Socket _socket;
        private SslStream? _sslStream;

        // See https://tools.ietf.org/html/rfc5246#appendix-A.4
        private const byte TlsHandshakeRecord = 0x16;

        public override async ValueTask<SingleStreamSocket> AcceptAsync(
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
                        throw new ConnectionLostException(RetryPolicy.AfterDelay(TimeSpan.Zero));
                    }
                    Debug.Assert(received == 1);
                    secure = buffer.Array![0] == TlsHandshakeRecord;
                }

                // If a secure connection is needed, a new SslSocket is created and returned from this method.
                // The caller is responsible for using the returned SslSocket in place of this TcpSocket.
                if (endpoint.IsSecure ?? secure)
                {
                    var socket = new SslSocket(this, _socket);
                    await socket.AcceptAsync(endpoint, authenticationOptions, cancel).ConfigureAwait(false);
                    _sslStream = socket.SslStream;
                    return socket;
                }
                else
                {
                    return this;
                }
            }
            catch (SocketException ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                throw new ConnectionRefusedException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
            catch (SocketException ex)
            {
                throw new ConnectFailedException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
        }

        public override ValueTask CloseAsync(Exception ex, CancellationToken cancel) => default;

        public override async ValueTask<SingleStreamSocket> ConnectAsync(
            Endpoint endpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Debug.Assert(_addr != null);

            try
            {
                // Connect to the peer and cache the description of the _socket.
                await _socket.ConnectAsync(_addr, cancel).ConfigureAwait(false);

                // If a secure socket is requested, create an SslSocket and return it from this method. The caller is
                // responsible for using the returned SslSocket instead of using this TcpSocket.

                if (authenticationOptions != null)
                {
                    var socket = new SslSocket(this, _socket);
                    await socket.ConnectAsync(endpoint, authenticationOptions, cancel).ConfigureAwait(false);
                    _sslStream = socket.SslStream;
                    return socket;
                }
                else
                {
                    return this;
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                throw new ConnectionRefusedException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
            catch (SocketException ex)
            {
                throw new ConnectFailedException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
            catch (Exception ex) when (cancel.IsCancellationRequested)
            {
                throw new OperationCanceledException(null, ex, cancel);
            }
            catch (Exception ex)
            {
                throw new TransportException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
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
                throw new ConnectionLostException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
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
                throw new TransportException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
            if (received == 0)
            {
                throw new ConnectionLostException(RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
            return received;
        }

        public override ValueTask<ArraySegment<byte>> ReceiveDatagramAsync(CancellationToken cancel) =>
            throw new InvalidOperationException("TCP doesn't support datagrams");

        public override async ValueTask<int> SendAsync(IList<ArraySegment<byte>> buffer, CancellationToken cancel)
        {
            try
            {
                // TODO: Use cancellable API once https://github.com/dotnet/runtime/issues/33417 is fixed.
                using CancellationTokenRegistration registration = cancel.Register(() => _socket.CloseNoThrow());
                return await _socket.SendAsync(buffer, SocketFlags.None).ConfigureAwait(false);
            }
            catch (SocketException ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
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
                throw new TransportException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
        }

        public override ValueTask<int> SendDatagramAsync(IList<ArraySegment<byte>> buffer, CancellationToken cancel) =>
            throw new InvalidOperationException("TCP doesn't support datagrams");

        protected override void Dispose(bool disposing) => _socket.Dispose();

        internal TcpSocket(Socket fd, ILogger logger, EndPoint? addr = null)
            : base(logger)
        {
            _addr = addr;

            // The socket is not connected if a client socket, it's connected otherwise.
            _socket = fd;
        }
    }
}
