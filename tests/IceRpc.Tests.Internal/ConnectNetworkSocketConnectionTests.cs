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
    public class ConnectNetworkSocketConnectionTests : ConnectionBaseTest
    {
        public ConnectNetworkSocketConnectionTests(
            Protocol protocol,
            string transport,
            bool tls,
            AddressFamily addressFamily)
            : base(protocol, transport, tls, addressFamily)
        {
        }

        [Test]
        public void ConnectNetworkSocketConnection_ConnectAsync_ConnectionRefusedException()
        {
            using NetworkSocket clientConnection = CreateClientConnection();
            Assert.ThrowsAsync<ConnectionRefusedException>(
                async () => await clientConnection.ConnectAsync(
                    ClientEndpoint,
                    ClientAuthenticationOptions,
                    default));
        }

        [Test]
        public void ConnectNetworkSocketConnection_ConnectAsync_OperationCanceledException()
        {
            using IListener listener = CreateListener();

            using var source = new CancellationTokenSource();
            if (!IsSecure && TransportName == "tcp")
            {
                // ConnectAsync might complete synchronously with TCP
            }
            else
            {
                using NetworkSocket clientConnection = CreateClientConnection();
                ValueTask<Endpoint> connectTask =
                    clientConnection.ConnectAsync(
                        ClientEndpoint,
                        ClientAuthenticationOptions,
                        source.Token);
                source.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await connectTask);
            }

            using var source2 = new CancellationTokenSource();
            source2.Cancel();
            using NetworkSocket clientConnection2 = CreateClientConnection();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await clientConnection2.ConnectAsync(
                    ClientEndpoint,
                    ClientAuthenticationOptions,
                    source2.Token));
        }

        private NetworkSocket CreateClientConnection() =>
           (Connection.DefaultClientTransport.CreateConnection(
                ClientEndpoint,
                ClientConnectionOptions,
                LogAttributeLoggerFactory.Instance) as NetworkSocketConnection)!.NetworkSocket;
    }
}
