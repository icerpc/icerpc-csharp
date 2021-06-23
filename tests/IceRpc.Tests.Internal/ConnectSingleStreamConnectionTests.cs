// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

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
    public class ConnectSingleStreamConnectionTests : ConnectionBaseTest
    {
        public ConnectSingleStreamConnectionTests(
            Protocol protocol,
            string transport,
            bool tls,
            AddressFamily addressFamily)
            : base(protocol, transport, tls, addressFamily)
        {
        }

        [Test]
        public void ConnectSingleStreamConnection_ConnectAsync_ConnectionRefusedException()
        {
            using NetworkSocket outgoingConnection = CreateOutgoingConnection();
            Assert.ThrowsAsync<ConnectionRefusedException>(
                async () => await outgoingConnection.ConnectAsync(
                    ClientEndpoint,
                    ClientAuthenticationOptions,
                    default));
        }

        [Test]
        public void ConnectSingleStreamConnection_ConnectAsync_OperationCanceledException()
        {
            using IListener listener = CreateListener();

            using var source = new CancellationTokenSource();
            if (!IsSecure && TransportName == "tcp")
            {
                // ConnectAsync might complete synchronously with TCP
            }
            else
            {
                using NetworkSocket outgoingConnection = CreateOutgoingConnection();
                ValueTask<Endpoint> connectTask =
                    outgoingConnection.ConnectAsync(
                        ClientEndpoint,
                        ClientAuthenticationOptions,
                        source.Token);
                source.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await connectTask);
            }

            using var source2 = new CancellationTokenSource();
            source2.Cancel();
            using NetworkSocket outgoingConnection2 = CreateOutgoingConnection();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await outgoingConnection2.ConnectAsync(
                    ClientEndpoint,
                    ClientAuthenticationOptions,
                    source2.Token));
        }

        private NetworkSocket CreateOutgoingConnection() =>
            (ClientEndpoint.TransportDescriptor!.OutgoingConnectionFactory!(
                ClientEndpoint,
                OutgoingConnectionOptions,
                Logger) as NetworkSocketConnection)!.Underlying;
    }
}
