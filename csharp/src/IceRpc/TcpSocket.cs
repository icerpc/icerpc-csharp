// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    internal sealed class TcpSocket : SingleStreamSocket
    {
        public override Socket Socket { get; }
        public override SslStream? SslStream => null;

        private readonly EndPoint? _addr;
        private readonly Communicator _communicator;
        private readonly Server? _server;
        private string _desc = "";
        // See https://tools.ietf.org/html/rfc5246#appendix-A.4
        private const byte TlsHandshakeRecord = 0x16;

        public override async ValueTask<SingleStreamSocket> AcceptAsync(Endpoint endpoint, CancellationToken cancel)
        {
            try
            {
                _desc = Network.SocketToString(Socket);

                // On the server side, when accepting a new connection for Ice2 endpoint, the TCP socket checks
                // the first byte sent by the peer to figure out if the peer tries to establish a TLS connection.
                bool secure = false;
                if (endpoint.Protocol == Protocol.Ice2)
                {
                    // Peek one byte into the tcp stream to see if it contains the TLS handshake record
                    var buffer = new ArraySegment<byte>(new byte[1]);
                    int received = await Socket.ReceiveAsync(buffer, SocketFlags.Peek, cancel).ConfigureAwait(false);
                    if (received == 0)
                    {
                        throw new ConnectionLostException(RetryPolicy.AfterDelay(TimeSpan.Zero));
                    }
                    Debug.Assert(received == 1);
                    secure = buffer.Array![0] == TlsHandshakeRecord;
                }

                // If a secure connection is needed, a new SslSocket is created and returned from this method.
                // The caller is responsible for using the returned SslSocket in place of this TcpSocket.
                SingleStreamSocket socket = this;
                if (endpoint.IsAlwaysSecure || secure)
                {
                    Debug.Assert(_server != null);
                    socket = new SslSocket(_server, this);
                    await socket.AcceptAsync(endpoint, cancel).ConfigureAwait(false);
                }

                if (endpoint.Communicator.TransportLogger.IsEnabled(LogLevel.Debug))
                {
                    endpoint.Communicator.TransportLogger.LogConnectionAccepted(endpoint.Transport,
                                                                                Network.LocalAddrToString(Socket),
                                                                                Network.RemoteAddrToString(Socket));
                }

                return socket;
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
            bool secure,
            CancellationToken cancel)
        {
            Debug.Assert(_addr != null);

            try
            {
                // Bind the socket to the source address if one is set.
                if ((endpoint as IPEndpoint)?.SourceAddress is IPAddress sourceAddress)
                {
                    Socket.Bind(new IPEndPoint(sourceAddress, 0));
                }

                // Connect to the peer and cache the description of the socket.
                await Socket.ConnectAsync(_addr, cancel).ConfigureAwait(false);
                _desc = Network.SocketToString(Socket);

                // If the endpoint is always secured or if a secure is requested, create an SslSocket and return
                // it from this method. The caller is responsible for using the returned SslSocket instead of
                // using this TcpSocket.

                SingleStreamSocket socket = this;
                if (endpoint.IsAlwaysSecure || secure)
                {
                    socket = new SslSocket(endpoint.Communicator, this);
                    await socket.ConnectAsync(endpoint, true, cancel).ConfigureAwait(false);
                }

                if (endpoint.Communicator.TransportLogger.IsEnabled(LogLevel.Debug))
                {
                    endpoint.Communicator.TransportLogger.LogConnectionEstablished(
                        endpoint.Transport,
                        Network.LocalAddrToString(Socket),
                        Network.RemoteAddrToString(Socket));
                }

                return socket;
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

        public override ValueTask<ArraySegment<byte>> ReceiveDatagramAsync(CancellationToken cancel) =>
            throw new InvalidOperationException("only supported by datagram transports");

        public override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (buffer.Length == 0)
            {
                throw new ArgumentException($"empty {nameof(buffer)}");
            }

            int received;
            try
            {
                received = await Socket.ReceiveAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
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

        public override async ValueTask<int> SendAsync(IList<ArraySegment<byte>> buffer, CancellationToken cancel)
        {
            try
            {
                // TODO: Use cancellable API once https://github.com/dotnet/runtime/issues/33417 is fixed.
                using CancellationTokenRegistration registration = cancel.Register(() => Socket.CloseNoThrow());
                return await Socket.SendAsync(buffer, SocketFlags.None).ConfigureAwait(false);
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

        public override string ToString() => _desc;

        protected override void Dispose(bool disposing) => Socket.Dispose();

        internal TcpSocket(Communicator communicator, EndPoint addr)
        {
            _communicator = communicator;
            _addr = addr;
            if (addr is IPEndPoint ipEndpoint)
            {
                Socket = Network.CreateSocket(false, ipEndpoint.AddressFamily);
            }
            else
            {
                Socket = Network.CreateSocket(false, null);
            }
            SetBufferSize(Transport.TCP,
                          _communicator.GetPropertyAsByteSize($"Ice.Tcp.RcvSize") ?? 0,
                          _communicator.GetPropertyAsByteSize($"Ice.Tcp.SndSize") ?? 0,
                          _communicator.Logger);
        }

        internal TcpSocket(Server server, Socket fd)
        {
            _communicator = server.Communicator;
            _server = server;
            Socket = fd;
            SetBufferSize(Transport.TCP,
                          _communicator.GetPropertyAsByteSize($"Ice.Tcp.RcvSize") ?? 0,
                          _communicator.GetPropertyAsByteSize($"Ice.Tcp.SndSize") ?? 0,
                          _communicator.Logger);
        }

        internal override IDisposable? StartScope(Endpoint endpoint)
        {
            // If any of the loggers is enabled we create the scope
            if (_communicator.TransportLogger.IsEnabled(LogLevel.Critical) ||
                _communicator.ProtocolLogger.IsEnabled(LogLevel.Critical) ||
                _communicator.SecurityLogger.IsEnabled(LogLevel.Critical) ||
                _communicator.LocationLogger.IsEnabled(LogLevel.Critical) ||
                _communicator.Logger.IsEnabled(LogLevel.Critical))
            {
                return _communicator.Logger.StartSocketScope(endpoint.Transport,
                                                             Network.LocalAddrToString(Socket),
                                                             Network.RemoteAddrToString(Socket));
            }
            return null;
        }
    }
}
