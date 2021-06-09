// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    // Testing the Ice1 and Ice2 protocol here is useful because of the handling of secure vs non-secure
    // incoming connection which is different (with Ice2, the acceptor peeks a byte on the connection to
    // figure out if the outgoing connection is a secure or non-secure connection).
    [TestFixture(Protocol.Ice2, "tcp", false, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "tcp", true, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "ws", false, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "ws", true, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "tcp", false, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "ssl", true, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "tcp", false, AddressFamily.InterNetworkV6)]
    [TestFixture(Protocol.Ice2, "tcp", false, AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class AcceptSingleStreamConnectionTests : ConnectionBaseTest
    {
        public AcceptSingleStreamConnectionTests(
            Protocol protocol,
            string transport,
            bool tls,
            AddressFamily addressFamily)
            : base(protocol, transport, tls, addressFamily)
        {
        }

        [Test]
        public async Task AcceptSingleStreamConnection_Acceptor_AcceptAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();
            ValueTask<SingleStreamConnection> acceptTask = CreateIncomingConnectionAsync(acceptor);

            using SingleStreamConnection outgoingConnection = CreateOutgoingConnection();
            ValueTask<(SingleStreamConnection, Endpoint)> connectTask = outgoingConnection.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);
            using SingleStreamConnection incomingConnection = await acceptTask;
        }

        [Test]
        public void AcceptSingleStreamConnection_Acceptor_Constructor_TransportException()
        {
            using IAcceptor acceptor = CreateAcceptor();
            Assert.Throws<TransportException>(() => CreateAcceptor());
        }

        [Test]
        public async Task AcceptSingleStreamConnection_AcceptAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();
            ValueTask<SingleStreamConnection> acceptTask = CreateIncomingConnectionAsync(acceptor);

            using SingleStreamConnection outgoingConnection = CreateOutgoingConnection();
            ValueTask<(SingleStreamConnection, Endpoint)> connectTask = outgoingConnection.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);

            using SingleStreamConnection incomingConnection = await acceptTask;

            ValueTask<(SingleStreamConnection, Endpoint?)> acceptTask2 = incomingConnection.AcceptAsync(
                ServerEndpoint,
                ServerAuthenticationOptions,
                default);

            await connectTask;

            if (ClientEndpoint.Protocol == Protocol.Ice2 && TransportName == "tcp")
            {
                await outgoingConnection.SendAsync(new List<ArraySegment<byte>> { new byte[1] }, default);
            }

            (SingleStreamConnection connection, Endpoint _) = await acceptTask2;

            // The SslConnection is returned if a secure connection is requested.
            if (IsSecure && TransportName != "ws")
            {
                Assert.IsInstanceOf<SslConnection>(connection);
            }
            else
            {
                Assert.IsNotInstanceOf<SslConnection>(connection);
            }
        }

        // We eventually retry this test if it fails. The AcceptAsync can indeed not always fail if for
        // example the server SSL handshake completes before the RST is received.
        [Test]
        public async Task AcceptSingleStreamConnection_AcceptAsync_ConnectionLostExceptionAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();
            ValueTask<SingleStreamConnection> acceptTask = CreateIncomingConnectionAsync(acceptor);

            SingleStreamConnection outgoingConnection = CreateOutgoingConnection();

            // We don't use outgoingConnection.ConnectAsync() here as this would start the TLS handshake for secure
            // connections and AcceptAsync would sometime succeed.
            await outgoingConnection.NetworkSocket!.ConnectAsync(
                new DnsEndPoint(ClientEndpoint.Host, ClientEndpoint.Port)).ConfigureAwait(false);

            using SingleStreamConnection incomingConnection = await acceptTask;

            outgoingConnection.Dispose();

            AsyncTestDelegate testDelegate;
            if (!IsSecure && ClientEndpoint.Protocol == Protocol.Ice1 && TransportName == "tcp")
            {
                // AcceptAsync is a no-op for Ice1 non-secure TCP connections so it won't throw.
                await incomingConnection.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    default);
                testDelegate = async () => await incomingConnection.ReceiveAsync(new byte[1], default);
            }
            else
            {
                testDelegate = async () => await incomingConnection.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    default);
            }
            Assert.ThrowsAsync<ConnectionLostException>(testDelegate);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void AcceptSingleStreamConnection_Acceptor_AddressReuse(bool wildcard1, bool wildcard2)
        {
            IAcceptor acceptor;
            if (wildcard1)
            {
                var serverData = new EndpointData(
                    ServerEndpoint.Transport,
                    "::0",
                    ServerEndpoint.Port,
                    ServerEndpoint.Data.Options);
                var serverEndpoint = TcpEndpoint.CreateEndpoint(serverData, ServerEndpoint.Protocol);
                acceptor = serverEndpoint.CreateAcceptor(IncomingConnectionOptions, Logger);
            }
            else
            {
                acceptor = CreateAcceptor();
            }

            if (wildcard2)
            {
                var serverData = new EndpointData(
                    ServerEndpoint.Transport,
                    "::0",
                    ServerEndpoint.Port,
                    ServerEndpoint.Data.Options);
                var serverEndpoint = TcpEndpoint.CreateEndpoint(serverData, ServerEndpoint.Protocol);

                if (OperatingSystem.IsMacOS())
                {
                    // On macOS, it's still possible to bind to a specific address even if a connection is bound
                    // to the wildcard address.
                    Assert.DoesNotThrow(
                        () => serverEndpoint.CreateAcceptor(IncomingConnectionOptions, Logger).Dispose());
                }
                else
                {
                    Assert.Catch<TransportException>(
                        () => serverEndpoint.CreateAcceptor(IncomingConnectionOptions, Logger));
                }
            }
            else
            {
                if (wildcard1 && OperatingSystem.IsMacOS())
                {
                    // On macOS, it's still possible to bind to a specific address even if a connection is bound
                    // to the wildcard address.
                    Assert.DoesNotThrow(() => CreateAcceptor().Dispose());
                }
                else
                {
                    Assert.Catch<TransportException>(() => CreateAcceptor());
                }
            }

            acceptor.Dispose();
        }

        [Test]
        public async Task AcceptSingleStreamConnection_AcceptAsync_OperationCanceledExceptionAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();

            using SingleStreamConnection outgoingConnection = CreateOutgoingConnection();
            ValueTask<(SingleStreamConnection, Endpoint)> connectTask = outgoingConnection.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);

            using SingleStreamConnection incomingConnection = await CreateIncomingConnectionAsync(acceptor);

            using var source = new CancellationTokenSource();
            source.Cancel();
            ValueTask<(SingleStreamConnection, Endpoint?)> acceptTask = incomingConnection.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    source.Token);

            if (!IsSecure && ClientEndpoint.Protocol == Protocol.Ice1 && TransportName == "tcp")
            {
                // AcceptAsync is a no-op for Ice1 non-secure TCP connections so it won't throw.
                await acceptTask;
            }
            else
            {
                Assert.CatchAsync<OperationCanceledException>(async () => await acceptTask);
            }
        }

        private SingleStreamConnection CreateOutgoingConnection() =>
            (ClientEndpoint.CreateOutgoingConnection(
                OutgoingConnectionOptions,
                Logger) as MultiStreamOverSingleStreamConnection)!.Underlying;

        private static async ValueTask<SingleStreamConnection> CreateIncomingConnectionAsync(IAcceptor acceptor) =>
            (await acceptor.AcceptAsync() as MultiStreamOverSingleStreamConnection)!.Underlying;
    }
}
