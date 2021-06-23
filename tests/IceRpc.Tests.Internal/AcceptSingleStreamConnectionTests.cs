// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    // Testing the Ice1 and Ice2 protocol here is useful because of the handling of secure vs non-secure
    // incoming connection which is different (with Ice2, the listener peeks a byte on the connection to
    // figure out if the outgoing connection is a secure or non-secure connection).
    [TestFixture(Protocol.Ice2, "tcp", false, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "tcp", true, AddressFamily.InterNetwork)]
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
        public async Task AcceptSingleStreamConnection_Listener_AcceptAsync()
        {
            using IListener listener = CreateListener();
            ValueTask<NetworkSocket> acceptTask = CreateIncomingConnectionAsync(listener);

            using NetworkSocket outgoingConnection = CreateOutgoingConnection();
            ValueTask<Endpoint> connectTask = outgoingConnection.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);
            using NetworkSocket incomingConnection = await acceptTask;
        }

        [Test]
        public void AcceptSingleStreamConnection_Listener_Constructor_TransportException()
        {
            using IListener listener = CreateListener();
            Assert.Throws<TransportException>(() => CreateListener());
        }

        [Test]
        public async Task AcceptSingleStreamConnection_AcceptAsync()
        {
            using IListener listener = CreateListener();
            ValueTask<NetworkSocket> acceptTask = CreateIncomingConnectionAsync(listener);

            using NetworkSocket outgoingConnection = CreateOutgoingConnection();
            ValueTask<Endpoint> connectTask = outgoingConnection.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);

            using NetworkSocket incomingConnection = await acceptTask;

            ValueTask<Endpoint?> acceptTask2 = incomingConnection.AcceptAsync(
                ServerEndpoint,
                ServerAuthenticationOptions,
                default);

            await connectTask;

            if (ClientEndpoint.Protocol == Protocol.Ice2 && TransportName == "tcp")
            {
                await outgoingConnection.SendAsync(new byte[1], default);
            }

            await acceptTask2;

            Assert.That(incomingConnection, Is.InstanceOf<TcpSocket>());
        }

        // We eventually retry this test if it fails. The AcceptAsync can indeed not always fail if for
        // example the server SSL handshake completes before the RST is received.
        [Test]
        public async Task AcceptSingleStreamConnection_AcceptAsync_ConnectionLostExceptionAsync()
        {
            using IListener listener = CreateListener();
            ValueTask<NetworkSocket> acceptTask = CreateIncomingConnectionAsync(listener);

            NetworkSocket outgoingConnection = CreateOutgoingConnection();

            // We don't use outgoingConnection.ConnectAsync() here as this would start the TLS handshake for secure
            // connections and AcceptAsync would sometime succeed.
            await outgoingConnection.Socket!.ConnectAsync(
                new DnsEndPoint(ClientEndpoint.Host, ClientEndpoint.Port)).ConfigureAwait(false);

            using NetworkSocket incomingConnection = await acceptTask;

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
        public void AcceptSingleStreamConnection_Listener_AddressReuse(bool wildcard1, bool wildcard2)
        {
            IListener listener;
            if (wildcard1)
            {
                var serverData = new EndpointData(
                    ServerEndpoint.Transport,
                    "::0",
                    ServerEndpoint.Port,
                    ServerEndpoint.Data.Options);
                var serverEndpoint = TcpEndpoint.CreateEndpoint(serverData, ServerEndpoint.Protocol);
                listener = serverEndpoint.TransportDescriptor!.ListenerFactory!(serverEndpoint,
                                                                                IncomingConnectionOptions,
                                                                                Logger);
            }
            else
            {
                listener = CreateListener();
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
                        () => serverEndpoint.TransportDescriptor!.ListenerFactory!(serverEndpoint,
                                                                                   IncomingConnectionOptions,
                                                                                   Logger).Dispose());
                }
                else
                {
                    Assert.Catch<TransportException>(
                        () => serverEndpoint.TransportDescriptor!.ListenerFactory!(serverEndpoint,
                                                                                   IncomingConnectionOptions,
                                                                                   Logger));
                }
            }
            else
            {
                if (wildcard1 && OperatingSystem.IsMacOS())
                {
                    // On macOS, it's still possible to bind to a specific address even if a connection is bound
                    // to the wildcard address.
                    Assert.DoesNotThrow(() => CreateListener().Dispose());
                }
                else
                {
                    Assert.Catch<TransportException>(() => CreateListener());
                }
            }

            listener.Dispose();
        }

        [Test]
        public async Task AcceptSingleStreamConnection_AcceptAsync_OperationCanceledExceptionAsync()
        {
            using IListener listener = CreateListener();

            using NetworkSocket outgoingConnection = CreateOutgoingConnection();
            ValueTask<Endpoint> connectTask = outgoingConnection.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);

            using NetworkSocket incomingConnection = await CreateIncomingConnectionAsync(listener);

            using var source = new CancellationTokenSource();
            source.Cancel();
            ValueTask<Endpoint?> acceptTask = incomingConnection.AcceptAsync(
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

        private NetworkSocket CreateOutgoingConnection() =>
            (ClientEndpoint.TransportDescriptor!.OutgoingConnectionFactory!(
                ClientEndpoint,
                OutgoingConnectionOptions,
                Logger) as NetworkSocketConnection)!.Underlying;

        private static async ValueTask<NetworkSocket> CreateIncomingConnectionAsync(IListener listener) =>
            (await listener.AcceptAsync() as NetworkSocketConnection)!.Underlying;
    }
}
