// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    // Test graceful close WS implementation. CloseAsync methods are no-ops for TCP/SSL and complete immediately
    // rather than waiting for the peer close notification so we can't test them like we do for WS.
    [TestFixture("ws", false)]
    [TestFixture("ws", true)]
    [Timeout(10000)]
    public class WSConnectionTests : SingleStreamConnectionBaseTest
    {
        public WSConnectionTests(string transport, bool tls)
            : base(Protocol.Ice2, transport, tls, AddressFamily.InterNetwork)
        {
        }

        [Test]
        public async Task WSSocket_CloseAsync()
        {
            ValueTask<int> serverReceiveTask = IncomingConnection.ReceiveAsync(new byte[1], default);
            ValueTask<int> clientReceiveTask = OutgoingConnection.ReceiveAsync(new byte[1], default);

            await OutgoingConnection.CloseAsync(0, default);

            // Wait for the server to send back a close frame.
            Assert.ThrowsAsync<ConnectionLostException>(async () => await clientReceiveTask);

            // Close the connection to unblock the incoming connection.
            OutgoingConnection.Dispose();

            Assert.ThrowsAsync<ConnectionLostException>(async () => await serverReceiveTask);
        }
    }
}
