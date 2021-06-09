// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    public class SingleStreamConnectionBaseTest : ConnectionBaseTest
    {
        protected SingleStreamConnection OutgoingConnection => _outgoingConnection!;
        protected SingleStreamConnection IncomingConnection => _incomingConnection!;
        private SingleStreamConnection? _outgoingConnection;
        private SingleStreamConnection? _incomingConnection;

        public SingleStreamConnectionBaseTest(
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
                _incomingConnection = ((MultiStreamOverSingleStreamConnection)CreateIncomingConnection()).Underlying;
                ValueTask<SingleStreamConnection> connectTask = SingleStreamConnectionAsync(ConnectAsync());
                _outgoingConnection = await connectTask;
            }
            else
            {
                ValueTask<SingleStreamConnection> connectTask = SingleStreamConnectionAsync(ConnectAsync());
                ValueTask<SingleStreamConnection> acceptTask = SingleStreamConnectionAsync(AcceptAsync());

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
