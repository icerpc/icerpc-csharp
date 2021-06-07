// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    public class SingleStreamSocketBaseTest : SocketBaseTest
    {
        protected SingleStreamSocket ClientSocket => _clientSocket!;
        protected SingleStreamSocket ServerSocket => _serverSocket!;
        private SingleStreamSocket? _clientSocket;
        private SingleStreamSocket? _serverSocket;

        public SingleStreamSocketBaseTest(
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
                _serverSocket = ((MultiStreamOverSingleStreamSocket)CreateServerSocket()).Underlying;
                ValueTask<SingleStreamSocket> connectTask = SingleStreamSocketAsync(ConnectAsync());
                _clientSocket = await connectTask;
            }
            else
            {
                ValueTask<SingleStreamSocket> connectTask = SingleStreamSocketAsync(ConnectAsync());
                ValueTask<SingleStreamSocket> acceptTask = SingleStreamSocketAsync(AcceptAsync());

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
