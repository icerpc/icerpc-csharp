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
        protected SingleStreamConnection ClientSocket => _clientSocket!;
        protected SingleStreamConnection ServerSocket => _serverSocket!;
        private SingleStreamConnection? _clientSocket;
        private SingleStreamConnection? _serverSocket;

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
                _serverSocket = ((MultiStreamOverSingleStreamConnection)CreateServerSocket()).Underlying;
                ValueTask<SingleStreamConnection> connectTask = SingleStreamSocketAsync(ConnectAsync());
                _clientSocket = await connectTask;
            }
            else
            {
                ValueTask<SingleStreamConnection> connectTask = SingleStreamSocketAsync(ConnectAsync());
                ValueTask<SingleStreamConnection> acceptTask = SingleStreamSocketAsync(AcceptAsync());

                _clientSocket = await connectTask;
                _serverSocket = await acceptTask;
            }
        }

        [TearDown]
        public void TearDown()
        {
            _clientSocket?.Dispose();
            _serverSocket?.Dispose();
        }
    }
}
