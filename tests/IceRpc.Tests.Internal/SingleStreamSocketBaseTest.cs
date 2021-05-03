// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    public class SingleStreamSocketBaseTest : SocketBaseTest
    {
        protected static readonly List<ArraySegment<byte>> OneBSendBuffer = new() { new byte[1] };
        protected static readonly List<ArraySegment<byte>> OneMBSendBuffer = new() { new byte[1024 * 1024] };
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
