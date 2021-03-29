// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    // Test graceful close WS implementation. CloseAsync methods are no-ops for TCP/SSL and complete immediately
    // rather than waiting for the peer close notification so we can't test them like we do for WS.
    [TestFixture("ws", NonSecure.Always)]
    [TestFixture("ws", NonSecure.Never)]
    public class WSSocketTests : SingleStreamSocketBaseTest
    {
        public WSSocketTests(string transport, NonSecure nonSecure)
            : base(Protocol.Ice2, transport, nonSecure, AddressFamily.InterNetwork)
        {
        }

        [Test]
        public async Task WSSocket_CloseAsync()
        {
            ValueTask<int> serverReceiveTask = ServerSocket.ReceiveAsync(new byte[1], default);
            ValueTask<int> clientReceiveTask = ClientSocket.ReceiveAsync(new byte[1], default);

            await ClientSocket.CloseAsync(new InvalidDataException(""), default);

            // Wait for the server to send back a close frame.
            Assert.ThrowsAsync<ConnectionLostException>(async () => await clientReceiveTask);

            // Close the socket to unblock the server socket.
            ClientSocket.Dispose();

            Assert.ThrowsAsync<ConnectionLostException>(async () => await serverReceiveTask);
        }
    }
}
