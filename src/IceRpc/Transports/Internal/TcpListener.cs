// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
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

        public async ValueTask<ISimpleNetworkConnection> AcceptAsync()
        {
            try
            {
                return _serverConnectionDecorator(
                    new TcpServerNetworkConnection(await _socket.AcceptAsync().ConfigureAwait(false),
                                                   Endpoint,
                                                   _idleTimeout,
                                                   _authenticationOptions));
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(default));
            }
        }

        public void Dispose() => _socket.Dispose();

        public override string ToString() => Endpoint.ToString();

        internal TcpListener(
            Endpoint endpoint,
            TcpOptions tcpOptions,
            SslServerAuthenticationOptions? authenticationOptions,
            Func<TcpServerNetworkConnection, ISimpleNetworkConnection> serverConnectionDecorator)
        {
            // We are not checking endpoint.Transport. The caller decided to give us this endpoint and we assume it's
            // a tcp or ssl endpoint regardless of its actual transport name.

            if (!IPAddress.TryParse(endpoint.Host, out IPAddress? ipAddress))
            {
                throw new NotSupportedException(
                    $"endpoint '{endpoint}' cannot accept connections because it has a DNS name");
            }

            // We always call ParseTcpParams to make sure the params are ok, even when Protocol is ice1.
            _ = endpoint.ParseTcpParams();

            _idleTimeout = tcpOptions.IdleTimeout;
            _serverConnectionDecorator = serverConnectionDecorator;

            if (authenticationOptions != null)
            {
                // Add the endpoint protocol to the SSL application protocols (used by TLS ALPN)
                authenticationOptions = authenticationOptions.Clone();
                authenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol>
                    {
                        new SslApplicationProtocol(endpoint.Protocol.Name)
                    };
            }
            _authenticationOptions = authenticationOptions;

            var address = new IPEndPoint(ipAddress, endpoint.Port);
            _socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    _socket.DualMode = !tcpOptions.IsIPv6Only;
                }

                _socket.ExclusiveAddressUse = true;

                if (tcpOptions.ReceiveBufferSize is int receiveSize)
                {
                    _socket.ReceiveBufferSize = receiveSize;
                }
                if (tcpOptions.SendBufferSize is int sendSize)
                {
                    _socket.SendBufferSize = sendSize;
                }

                _socket.Bind(address);
                address = (IPEndPoint)_socket.LocalEndPoint!;
                _socket.Listen(tcpOptions.ListenerBackLog);
            }
            catch (SocketException ex)
            {
                _socket.Dispose();
                throw new TransportException(ex);
            }

            Endpoint = endpoint with { Port = (ushort)address.Port };
        }
    }
}
