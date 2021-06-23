// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    public class NetworkSocketConnectionBaseTest : ConnectionBaseTest
    {
        protected NetworkSocket OutgoingConnection => _outgoingConnection!;
        protected NetworkSocket IncomingConnection => _incomingConnection!;
        private NetworkSocket? _outgoingConnection;
        private NetworkSocket? _incomingConnection;

        public NetworkSocketConnectionBaseTest(
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
            if (ClientEndpoint.IsDatagram)
            {
                _incomingConnection = ((NetworkSocketConnection)CreateIncomingConnection()).Underlying;
                ValueTask<NetworkSocket> connectTask = NetworkSocketConnectionAsync(ConnectAsync());
                _outgoingConnection = await connectTask;
            }
            else
            {
                ValueTask<NetworkSocket> connectTask = NetworkSocketConnectionAsync(ConnectAsync());
                ValueTask<NetworkSocket> acceptTask = NetworkSocketConnectionAsync(AcceptAsync());

                _outgoingConnection = await connectTask;
                _incomingConnection = await acceptTask;
            }
        }

        [TearDown]
        public void TearDown()
        {
            _outgoingConnection?.Dispose();
            _incomingConnection?.Dispose();
        }
    }
}
