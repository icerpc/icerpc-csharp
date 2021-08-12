// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the TCP transport.</summary>
    internal sealed class TcpListener : IListener
    {
        public Endpoint Endpoint { get; }

        private readonly ILogger _logger;
        private readonly ServerConnectionOptions _connectionOptions;
        private readonly Socket _socket;
        private readonly TcpOptions _tcpOptions;
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

            return NetworkSocketConnection.FromNetworkSocket(
                tcpSocket,
                Endpoint,
                _connectionOptions,
                _tcpOptions);
        }

        public void Dispose() => _socket.Dispose();

        public override string ToString() => Endpoint.ToString();

        internal TcpListener(
            Socket socket,
            Endpoint endpoint,
            ILogger logger,
            ServerConnectionOptions connectionOptions,
            TcpOptions tcpOptions)
        {
            Endpoint = endpoint;
            _logger = logger;
            _connectionOptions = connectionOptions;
            _tcpOptions = tcpOptions;
            _socket = socket;

            // We always call ParseTcpParams to make sure the params are ok, even when Protocol is ice1.

            _tls = endpoint.ParseTcpParams().Tls;

            if (endpoint.Protocol == Protocol.Ice1)
            {
                _tls = endpoint.Transport == TransportNames.Ssl;
            }
        }
    }
}
