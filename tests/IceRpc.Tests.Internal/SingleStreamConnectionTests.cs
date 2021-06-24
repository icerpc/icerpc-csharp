// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    // Test the various single stream connection implementations. We don't test Ice1 + WS here as it doesn't really
    // provide additional test coverage given that the WS connection has no protocol specific code.
    [TestFixture("tcp", false)]
    [TestFixture("tcp", true)]
    [TestFixture("udp", false)]
    [Timeout(10000)]
    public class NetworkSocketConnectionTests : NetworkSocketConnectionBaseTest
    {
        public NetworkSocketConnectionTests(string transport, bool tls)
            : base(transport == "udp" ? Protocol.Ice1 : Protocol.Ice2, transport, tls)
        {
        }

        [Test]
        public async Task NetworkSocketConnection_CloseAsync_ExceptionAsync()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                // This will either complete successfully or with an OperationCanceledException
                await ClientConnection.CloseAsync(0, canceled.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        [Test]
        public void NetworkSocketConnection_Dispose()
        {
            ClientConnection.Dispose();
            ServerConnection.Dispose();
            ClientConnection.Dispose();
            ServerConnection.Dispose();
        }

        [Test]
        public void NetworkSocketConnection_Properties()
        {
            Test(ClientConnection);
            Test(ServerConnection);

            static void Test(NetworkSocket connection)
            {
                Assert.NotNull(connection.ConnectionInformation);
                Assert.IsNotEmpty(connection.ToString());
            }
        }
    }
}
