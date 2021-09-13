// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System.Net.Sockets;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    public class SocketConnectionBaseTest : ConnectionBaseTest
    {
        protected NetworkSocket ClientConnection => _clientConnection!;
        protected NetworkSocket ServerConnection => _serverConnection!;
        private NetworkSocket? _clientConnection;
        private NetworkSocket? _serverConnection;

        public SocketConnectionBaseTest(
            Protocol protocol,
            string transport,
            bool tls,
            AddressFamily addressFamily = AddressFamily.InterNetwork)
            : base(protocol, transport, tls, addressFamily)
        {
        }

        [SetUp]
        public async Task SetupAsync()
        {
            if (ClientEndpoint.Transport == "udp")
            {
                _serverConnection = ((SocketConnection)CreateServerConnection()).NetworkSocket;
                ValueTask<NetworkSocket> connectTask = SocketConnectionAsync(ConnectAsync());
                _clientConnection = await connectTask;
            }
            else
            {
                ValueTask<NetworkSocket> connectTask = SocketConnectionAsync(ConnectAsync());
                ValueTask<NetworkSocket> acceptTask = SocketConnectionAsync(AcceptAsync());

                _clientConnection = await connectTask;
                _serverConnection = await acceptTask;
            }
        }

        [TearDown]
        public void TearDown()
        {
            _clientConnection?.Dispose();
            _serverConnection?.Dispose();
        }
    }
}
