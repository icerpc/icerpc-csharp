// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    // Test the varions single socket implementations. We don't test Ice1 + WS here as it doesn't really
    // provide additional test coverage given that the WS socket has no protocol specific code.
    [TestFixture("tcp", NonSecure.Always)]
    [TestFixture("ws", NonSecure.Always)]
    [TestFixture("tcp", NonSecure.Never)]
    [TestFixture("ws", NonSecure.Never)]
    [TestFixture("udp", NonSecure.Always)]
    [Timeout(5000)]
    public class SingleStreamSocketTests : SingleStreamSocketBaseTest
    {
        public SingleStreamSocketTests(string transport, NonSecure nonSecure)
            : base(transport == "udp" ? Protocol.Ice1 : Protocol.Ice2, transport, nonSecure)
        {
        }

        [Test]
        public async Task SingleStreamSocket_CloseAsync_ExceptionAsync()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                // This will either complete successfully or with an OperationCanceledException
                await ClientSocket.CloseAsync(new InvalidDataException(""), canceled.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        [Test]
        public void SingleStreamSocket_Dispose()
        {
            ClientSocket.Dispose();
            ServerSocket.Dispose();
            ClientSocket.Dispose();
            ServerSocket.Dispose();
        }

        [Test]
        public void SingleStreamSocket_Properties()
        {
            Test(ClientSocket);
            Test(ServerSocket);

            void Test(SingleStreamSocket socket)
            {
                Assert.NotNull(socket.Socket);
                Assert.AreEqual(socket.SslStream != null, IsSecure);
                Assert.IsNotEmpty(socket.ToString());
            }
        }
    }
}
