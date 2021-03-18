// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IceRpc
{
    internal class TcpAcceptor : IAcceptor
    {
        public Endpoint Endpoint { get; }

        private readonly ILogger _logger;
        private readonly Server _server;
        private readonly Socket _socket;
        private readonly IPEndPoint _addr;

        public async ValueTask<Connection> AcceptAsync()
        {
            try
            {
                Socket fd = await _socket.AcceptAsync().ConfigureAwait(false);

                var socket = ((TcpEndpoint)Endpoint).CreateSocket(fd);
                MultiStreamOverSingleStreamSocket multiStreamSocket = Endpoint.Protocol switch
                {
                    Protocol.Ice1 => new Ice1NetworkSocket(
                        Endpoint,
                        _server.Communicator.TransportLogger,
                        _server.IncomingFrameMaxSize,
                        isIncoming: true,
                        socket,
                        _server.BidirectionalStreamMaxCount,
                        _server.UnidirectionalStreamMaxCount),
                    _ => new SlicSocket(
                        Endpoint,
                        Endpoint.Communicator.TransportLogger,
                        _server.IncomingFrameMaxSize,
                        isIncoming: true,
                        socket,
                        _server.BidirectionalStreamMaxCount,
                        _server.UnidirectionalStreamMaxCount)
                };
                return ((TcpEndpoint)Endpoint).CreateConnection(multiStreamSocket, label: null, _server);
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogAcceptingConnectionFailed(Endpoint.Transport, Network.LocalAddrToString(_addr), ex);
                }
                throw;
            }
        }

        public void Dispose() => _socket.CloseNoThrow();

        public override string ToString() => _addr.ToString();

        internal TcpAcceptor(Socket socket, TcpEndpoint endpoint, Server server)
        {
            Debug.Assert(!endpoint.HasDnsHost); // not a DNS name

            _logger = server.Communicator.TransportLogger;
            _server = server;
            _addr = (IPEndPoint)socket.LocalEndPoint!;
            _socket = socket;

            Endpoint = endpoint;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogAcceptingConnection(Endpoint.Transport, Network.LocalAddrToString(_addr));
            }
        }
    }
}
