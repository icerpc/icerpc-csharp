// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the TCP transport.</summary>
    internal sealed class TcpListener : IListener<ISimpleNetworkConnection>
    {
        public Endpoint Endpoint { get; }

        private readonly SslServerAuthenticationOptions? _authenticationOptions;
        private readonly TimeSpan _idleTimeout;
        private readonly Func<TcpServerNetworkConnection, ISimpleNetworkConnection> _serverConnectionDecorator;
        private readonly Socket _socket;

        // tls parsed from endpoint
        private readonly bool? _tls;

        public async Task<ISimpleNetworkConnection> AcceptAsync()
        {
            Socket acceptedSocket;
            try
            {
                acceptedSocket = await _socket.AcceptAsync().ConfigureAwait(false);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                // We translate this expected error into an ObjectDisposedException that the caller can safely catch and
                // ignore.
                throw new ObjectDisposedException(nameof(TcpListener), ex);
            }

            // We don't translate other exceptions since they are unexpected and the application code has no opportunity
            // to catch and handle them. They are only useful for the log decorator.

            return _serverConnectionDecorator(
#pragma warning disable CA2000 // the caller will Dispose the connection and _serverConnectionDecorator never throws
                new TcpServerNetworkConnection(acceptedSocket,
                                               Endpoint,
                                               _tls,
                                               _idleTimeout,
                                               _authenticationOptions));
#pragma warning restore CA2000
        }

        public ValueTask DisposeAsync()
        {
            _socket.Dispose();
            return default;
        }

        public override string ToString() => Endpoint.ToString();

        internal TcpListener(
            Endpoint endpoint,
            TcpServerOptions options,
            Func<TcpServerNetworkConnection, ISimpleNetworkConnection> serverConnectionDecorator)
        {
            // We are not checking endpoint.Transport. The caller decided to give us this endpoint and we assume it's
            // a tcp or ssl endpoint regardless of its actual transport name.

            if (!IPAddress.TryParse(endpoint.Host, out IPAddress? ipAddress))
            {
                throw new NotSupportedException(
                    $"endpoint '{endpoint}' cannot accept connections because it has a DNS name");
            }

            _tls = endpoint.ParseTcpParams().Tls;
            _idleTimeout = options.IdleTimeout;

            _serverConnectionDecorator = serverConnectionDecorator;

            _authenticationOptions = options.AuthenticationOptions;

            if (_authenticationOptions != null)
            {
                // Add the endpoint protocol to the SSL application protocols (used by TLS ALPN)
                _authenticationOptions = _authenticationOptions.Clone();
                _authenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol>
                    {
                        new SslApplicationProtocol(endpoint.Scheme.Name)
                    };
            }

            var address = new IPEndPoint(ipAddress, endpoint.Port);
            _socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    _socket.DualMode = !options.IsIPv6Only;
                }

                _socket.ExclusiveAddressUse = true;

                if (options.ReceiveBufferSize is int receiveSize)
                {
                    _socket.ReceiveBufferSize = receiveSize;
                }
                if (options.SendBufferSize is int sendSize)
                {
                    _socket.SendBufferSize = sendSize;
                }

                _socket.Bind(address);
                address = (IPEndPoint)_socket.LocalEndPoint!;
                _socket.Listen(options.ListenerBackLog);
            }
            catch (SocketException ex)
            {
                _socket.Dispose();
                throw ex.ToTransportException(default);
            }

            Endpoint = endpoint with { Port = (ushort)address.Port };
        }
    }
}
