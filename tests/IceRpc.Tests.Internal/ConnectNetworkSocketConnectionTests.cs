// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System.Net.Sockets;

namespace IceRpc.Tests.Internal
{
    // Testing the Ice1 and Ice2 protocol here is useful because of the handling of secure vs non-secure incoming
    // connections: with Ice2, the listener peeks a byte on the connection to figure out if it's secure or not.
    [TestFixture(Protocol.Ice1, "tcp", false, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "ssl", true, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "tcp", false, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "tcp", true, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "tcp", false, AddressFamily.InterNetworkV6)]
    [TestFixture(Protocol.Ice2, "tcp", true, AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class ConnectSocketConnectionTests : ConnectionBaseTest
    {
        public ConnectSocketConnectionTests(
            Protocol protocol,
            string transport,
            bool tls,
            AddressFamily addressFamily)
            : base(protocol, transport, tls, addressFamily)
        {
        }

        [Test]
        public void ConnectSocketConnection_ConnectAsync_ConnectionRefusedException()
        {
            using NetworkSocket clientSocket = CreateNetworkSocket();
            Assert.ThrowsAsync<ConnectionRefusedException>(
                async () => await clientSocket.ConnectAsync(ClientEndpoint, default));
        }

        [Test]
        public void ConnectSocketConnection_ConnectAsync_OperationCanceledException()
        {
            using IListener listener = CreateListener();

            using var source = new CancellationTokenSource();
            if (!IsSecure && TransportName == "tcp")
            {
                // ConnectAsync might complete synchronously with TCP
            }
            else
            {
                using NetworkSocket clientSocket = CreateNetworkSocket();
                ValueTask<Endpoint> connectTask =
                    clientSocket.ConnectAsync(ClientEndpoint, source.Token);
                source.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await connectTask);
            }

            using var source2 = new CancellationTokenSource();
            source2.Cancel();
            using NetworkSocket clientSocket2 = CreateNetworkSocket();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await clientSocket2.ConnectAsync(ClientEndpoint, source2.Token));
        }

        private NetworkSocket CreateNetworkSocket() =>
           ((SocketConnection)CreateClientConnection()).NetworkSocket;
    }
}
