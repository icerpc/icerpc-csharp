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

        private readonly Server _server;
        private readonly Socket _socket;
        private readonly IPEndPoint _addr;

        public async ValueTask<Connection> AcceptAsync()
        {
            try
            {
                Socket fd = await _socket.AcceptAsync().ConfigureAwait(false);

                var options = _server.ConnectionOptions;

                var socket = ((TcpEndpoint)Endpoint).CreateSocket(fd, options.SocketOptions, options.TransportLogger);

                MultiStreamOverSingleStreamSocket multiStreamSocket = Endpoint.Protocol switch
                {
                    Protocol.Ice1 => new Ice1NetworkSocket(Endpoint, socket, options),
                    _ => new SlicSocket(Endpoint, socket, options)
                };
                return ((TcpEndpoint)Endpoint).CreateConnection(multiStreamSocket, options, _server);
            }
            catch (Exception ex)
            {
                if (_server.ConnectionOptions.TransportLogger.IsEnabled(LogLevel.Error))
                {
                    _server.ConnectionOptions.TransportLogger.LogAcceptingConnectionFailed(
                        Endpoint.Transport,
                        Network.LocalAddrToString(_addr), ex);
                }
                throw;
            }
        }

        public void Dispose() => _socket.CloseNoThrow();

        public override string ToString() => _addr.ToString();

        internal TcpAcceptor(Socket socket, TcpEndpoint endpoint, Server server)
        {
            Debug.Assert(!endpoint.HasDnsHost); // not a DNS name

            Endpoint = endpoint;

            _server = server;
            _addr = (IPEndPoint)socket.LocalEndPoint!;
            _socket = socket;

            if (server.ConnectionOptions.TransportLogger.IsEnabled(LogLevel.Debug))
            {
                server.ConnectionOptions.TransportLogger.LogAcceptingConnection(
                    Endpoint.Transport,
                    Network.LocalAddrToString(_addr));
            }
        }
    }
}
