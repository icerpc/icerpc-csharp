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

                var socket = ((TcpEndpoint)Endpoint).CreateSocket(_server, fd);
                MultiStreamOverSingleStreamSocket multiStreamSocket = Endpoint.Protocol switch
                {
                    Protocol.Ice1 => new Ice1NetworkSocket(socket, Endpoint, _server),
                    _ => new SlicSocket(socket, Endpoint, _server)
                };
                return ((TcpEndpoint)Endpoint).CreateConnection(multiStreamSocket, label: null, _server);
            }
            catch (Exception ex)
            {
                if (_server.Communicator.TransportLogger.IsEnabled(LogLevel.Error))
                {
                    _server.Communicator.TransportLogger.LogAcceptingConnectionFailed(
                        Endpoint.Transport,
                        Network.LocalAddrToString(_addr),
                        ex);
                }
                throw;
            }
        }

        public void Dispose() => _socket.CloseNoThrow();

        public override string ToString() => _addr.ToString();

        internal TcpAcceptor(TcpEndpoint endpoint, Server server)
        {
            Debug.Assert(!endpoint.HasDnsHost); // not a DNS name

            _server = server;
            _addr = new IPEndPoint(endpoint.Address, endpoint.Port);
            _socket = Network.CreateServerSocket(endpoint, _addr.AddressFamily);

            try
            {
                _socket.Bind(_addr);
                _addr = (IPEndPoint)_socket.LocalEndPoint!;
                _socket.Listen(endpoint.Communicator.GetPropertyAsInt("Ice.TCP.Backlog") ?? 511);
            }
            catch (SocketException ex)
            {
                _socket.CloseNoThrow();
                throw new TransportException(ex);
            }

            Endpoint = endpoint.Clone((ushort)_addr.Port);

            if (_server.Communicator.TransportLogger.IsEnabled(LogLevel.Debug))
            {
                _server.Communicator.TransportLogger.LogAcceptingConnection(
                    Endpoint.Transport,
                    Network.LocalAddrToString(_addr));
            }
        }
    }
}
