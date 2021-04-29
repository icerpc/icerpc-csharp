// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IceRpc
{
    internal class TcpAcceptor : IAcceptor
    {
        public Endpoint Endpoint { get; }
        internal IPEndPoint IPEndPoint { get; }

        private readonly Server _server;
        private readonly Socket _socket;

        public async ValueTask<Connection> AcceptAsync()
        {
            Socket fd = await _socket.AcceptAsync().ConfigureAwait(false);

            ConnectionOptions options = _server.ConnectionOptions;

            SingleStreamSocket socket = ((TcpEndpoint)Endpoint).CreateSocket(fd, _server.Logger);

            MultiStreamOverSingleStreamSocket multiStreamSocket = Endpoint.Protocol switch
            {
                Protocol.Ice1 => new Ice1NetworkSocket(Endpoint, socket, options),
                _ => new SlicSocket(Endpoint, socket, options)
            };
            return ((TcpEndpoint)Endpoint).CreateConnection(multiStreamSocket, options, _server);
        }

        public void Dispose() => _socket.CloseNoThrow();

        public override string ToString() => $"{base.ToString()} {IPEndPoint}";

        internal TcpAcceptor(Socket socket, TcpEndpoint endpoint, Server server)
        {
            Debug.Assert(!endpoint.HasDnsHost); // not a DNS name

            Endpoint = endpoint;

            _server = server;
            IPEndPoint = (IPEndPoint)socket.LocalEndPoint!;
            _socket = socket;
        }
    }
}
