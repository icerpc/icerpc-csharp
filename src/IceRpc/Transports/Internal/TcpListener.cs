// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

using static IceRpc.Transports.Internal.TcpUtils;

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the TCP transport.</summary>
    internal sealed class TcpListener : IListener
    {
        public EndpointRecord Endpoint { get; }

        private readonly ILogger _logger;
        private readonly ServerConnectionOptions _options;
        private readonly Socket _socket;
        private readonly bool? _tls;

        public async ValueTask<MultiStreamConnection> AcceptAsync()
        {
            TcpSocket tcpSocket;
            try
            {
                tcpSocket = new TcpSocket(await _socket.AcceptAsync().ConfigureAwait(false), _logger, _tls);
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(default));
            }

            return NetworkSocketConnection.FromNetworkSocket(tcpSocket, Endpoint, _options);
        }

        public void Dispose() => _socket.Dispose();

        public override string ToString() => Endpoint.ToString();

        internal TcpListener(Socket socket, EndpointRecord endpoint, ILogger logger, ServerConnectionOptions options)
        {
            Endpoint = endpoint;
            _logger = logger;
            _options = options;
            _socket = socket;
            _tls = ParseLocalTcpParameters(endpoint);

            if (endpoint.Protocol == Protocol.Ice1)
            {
                _tls = endpoint.Transport == TransportNames.Ssl;
            }

            _ = ParseTcpParameters(endpoint);
        }
    }
}
