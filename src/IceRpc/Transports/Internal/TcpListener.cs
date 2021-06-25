// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the TCP transport.</summary>
    internal sealed class TcpListener : IListener
    {
        public Endpoint Endpoint { get; }

        private readonly ILogger _logger;
        private readonly ServerConnectionOptions _options;
        private readonly Socket _socket;

        public async ValueTask<MultiStreamConnection> AcceptAsync()
        {
            TcpSocket tcpSocket;
            try
            {
                tcpSocket = new TcpSocket(await _socket.AcceptAsync().ConfigureAwait(false), _logger);
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(default));
            }

            return NetworkSocketConnection.FromNetworkSocket(tcpSocket, Endpoint, _options);
        }

        public void Dispose() => _socket.Dispose();

        public override string ToString() => Endpoint.ToString();

        internal TcpListener(Socket socket, Endpoint endpoint, ILogger logger, ServerConnectionOptions options)
        {
            Endpoint = endpoint;
            _logger = logger;
            _options = options;
            _socket = socket;
        }
    }
}
