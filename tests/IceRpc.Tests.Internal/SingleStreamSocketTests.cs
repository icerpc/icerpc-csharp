// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    // Test the varions single socket implementations. We don't test Ice1 + WS here as it doesn't really
    // provide additional test coverage given that the WS socket has no protocol specific code.
    [TestFixture("tcp", false)]
    [TestFixture("ws", false)]
    [TestFixture("tcp", true)]
    [TestFixture("ws", true)]
    [TestFixture("udp", false)]
    [Timeout(10000)]
    public class SingleStreamSocketTests : SingleStreamSocketBaseTest
    {
        public SingleStreamSocketTests(string transport, bool tls)
            : base(transport == "udp" ? Protocol.Ice1 : Protocol.Ice2, transport, tls)
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
                await ClientSocket.CloseAsync(0, canceled.Token);
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

            static void Test(SingleStreamSocket socket)
            {
                Assert.NotNull(socket.Socket);
                Assert.IsNotEmpty(socket.ToString());
            }
        }
    }
}
