// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
            Socket fd = await _socket.AcceptAsync().ConfigureAwait(false);
            var socket = ((TcpEndpoint)Endpoint).CreateSocket(fd);
            MultiStreamOverSingleStreamSocket multiStreamSocket = Endpoint.Protocol switch
            {
                Protocol.Ice1 => new Ice1NetworkSocket(socket, Endpoint, _adapter),
                _ => new SlicSocket(socket, Endpoint, _adapter)
            };
            return ((TcpEndpoint)Endpoint).CreateConnection(multiStreamSocket, label: null, _adapter);
        }

        public void Dispose() => _socket.CloseNoThrow();

        public IDisposable? StartScope()
        {
            if (Endpoint.Communicator.Logger.IsEnabled(LogLevel.Critical))
            {
                Endpoint.Communicator.Logger.StartTcpAcceptorScope(_adapter.Name,
                                                                   Network.LocalAddrToString(_addr),
                                                                   Endpoint.Transport,
                                                                   Endpoint.Protocol);
            }
            return null;
        }

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
