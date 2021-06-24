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
    public class SingleStreamConnectionTests : SingleStreamConnectionBaseTest
    {
        public SingleStreamConnectionTests(string transport, bool tls)
            : base(transport == "udp" ? Protocol.Ice1 : Protocol.Ice2, transport, tls)
        {
        }

        [Test]
        public async Task SingleStreamConnection_CloseAsync_ExceptionAsync()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                // This will either complete successfully or with an OperationCanceledException
                await OutgoingConnection.CloseAsync(0, canceled.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        [Test]
        public void SingleStreamConnection_Dispose()
        {
            OutgoingConnection.Dispose();
            IncomingConnection.Dispose();
            OutgoingConnection.Dispose();
            IncomingConnection.Dispose();
        }

        [Test]
        public void SingleStreamConnection_Properties()
        {
            Test(OutgoingConnection);
            Test(IncomingConnection);

            static void Test(SingleStreamConnection connection)
            {
                Assert.NotNull(connection.ConnectionInformation);
                Assert.IsNotEmpty(connection.ToString());
            }
        }
    }
}
