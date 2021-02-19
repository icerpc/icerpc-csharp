// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ZeroC.Ice
{
    internal class TcpAcceptor : IAcceptor
    {
        public Endpoint Endpoint { get; }

        private readonly ObjectAdapter _adapter;
        private readonly Socket _socket;
        private readonly IPEndPoint _addr;

        public async ValueTask<Connection> AcceptAsync()
        {
            ILogger transportLogger = _adapter.Communicator.TransportLogger;

            try
            {
                if (transportLogger.IsEnabled(LogLevel.Debug))
                {
                    transportLogger.LogAcceptingConnection(Endpoint.Transport, Network.LocalAddrToString(_addr));
                }

                Socket fd = await _socket.AcceptAsync().ConfigureAwait(false);

                var socket = ((TcpEndpoint)Endpoint).CreateSocket(fd);
                MultiStreamOverSingleStreamSocket multiStreamSocket = Endpoint.Protocol switch
                {
                    Protocol.Ice1 => new Ice1NetworkSocket(socket, Endpoint, _adapter),
                    _ => new SlicSocket(socket, Endpoint, _adapter)
                };
                return ((TcpEndpoint)Endpoint).CreateConnection(multiStreamSocket, label: null, _adapter);
            }
            catch (Exception ex)
            {
                if (transportLogger.IsEnabled(LogLevel.Error))
                {
                    transportLogger.LogAcceptingConnectionFailed(
                        Endpoint.Transport,
                        Network.LocalAddrToString(_addr),
                        ex);
                }
                throw;
            }
        }

        public void Dispose() => _socket.CloseNoThrow();

        public override string ToString() => _addr.ToString();

        internal TcpAcceptor(TcpEndpoint endpoint, ObjectAdapter adapter)
        {
            Debug.Assert(endpoint.Address != IPAddress.None); // not a DNS name

            _adapter = adapter;
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
        }
    }
}
