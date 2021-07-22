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
    // server connection which is different (with Ice2, the listener peeks a byte on the connection to
    // figure out if the client connection is a secure or non-secure connection).
    [TestFixture(Protocol.Ice2, "tcp", false, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "tcp", true, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "tcp", false, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "ssl", true, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "tcp", false, AddressFamily.InterNetworkV6)]
    [TestFixture(Protocol.Ice2, "tcp", false, AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class AcceptNetworkSocketConnectionTests : ConnectionBaseTest
    {
        public AcceptNetworkSocketConnectionTests(
            Protocol protocol,
            string transport,
            bool tls,
            AddressFamily addressFamily)
            : base(protocol, transport, tls, addressFamily)
        {
        }

        [Test]
        public async Task AcceptNetworkSocketConnection_Listener_AcceptAsync()
        {
            using IListener listener = CreateListener();
            ValueTask<NetworkSocket> acceptTask = CreateServerConnectionAsync(listener);

            using NetworkSocket clientConnection = CreateClientConnection();
            ValueTask<Endpoint> connectTask = clientConnection.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);
            using NetworkSocket serverConnection = await acceptTask;
        }

        [Test]
        public void AcceptNetworkSocketConnection_Listener_Constructor_TransportException()
        {
            using IListener listener = CreateListener();
            Assert.Throws<TransportException>(() => CreateListener());
        }

        [Test]
        public async Task AcceptNetworkSocketConnection_AcceptAsync()
        {
            using IListener listener = CreateListener();
            ValueTask<NetworkSocket> acceptTask = CreateServerConnectionAsync(listener);

            using NetworkSocket clientConnection = CreateClientConnection();
            ValueTask<Endpoint> connectTask = clientConnection.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);

            using NetworkSocket serverConnection = await acceptTask;

            ValueTask<Endpoint?> acceptTask2 = serverConnection.AcceptAsync(
                ServerEndpoint,
                ServerAuthenticationOptions,
                default);

            await connectTask;

            if (ClientEndpoint.Protocol == Protocol.Ice2 && TransportName == "tcp")
            {
                await clientConnection.SendAsync(new byte[1], default);
            }

            await acceptTask2;

            Assert.That(serverConnection, Is.InstanceOf<TcpSocket>());
        }

        // We eventually retry this test if it fails. The AcceptAsync can indeed not always fail if for
        // example the server SSL handshake completes before the RST is received.
        [Test]
        public async Task AcceptNetworkSocketConnection_AcceptAsync_ConnectionLostExceptionAsync()
        {
            using IListener listener = CreateListener();
            ValueTask<NetworkSocket> acceptTask = CreateServerConnectionAsync(listener);

            NetworkSocket clientConnection = CreateClientConnection();

            // We don't use clientConnection.ConnectAsync() here as this would start the TLS handshake for secure
            // connections and AcceptAsync would sometime succeed.
            await clientConnection.Socket!.ConnectAsync(
                new DnsEndPoint(ClientEndpoint.Host, ClientEndpoint.Port)).ConfigureAwait(false);

            using NetworkSocket serverConnection = await acceptTask;

            clientConnection.Dispose();

            AsyncTestDelegate testDelegate;
            if (!IsSecure && ClientEndpoint.Protocol == Protocol.Ice1 && TransportName == "tcp")
            {
                // AcceptAsync is a no-op for Ice1 non-secure TCP connections so it won't throw.
                await serverConnection.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    default);
                testDelegate = async () => await serverConnection.ReceiveAsync(new byte[1], default);
            }
            else
            {
                testDelegate = async () => await serverConnection.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    default);
            }
            Assert.ThrowsAsync<ConnectionLostException>(testDelegate);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void AcceptNetworkSocketConnection_Listener_AddressReuse(bool wildcard1, bool wildcard2)
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
                listener = Server.DefaultServerTransport.Listen(serverEndpoint,
                                                                ServerConnectionOptions,
                                                                Logger).Listener!;
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
                        () => Server.DefaultServerTransport.Listen(serverEndpoint,
                                                                   ServerConnectionOptions,
                                                                   Logger).Listener!.Dispose());
                }
                else
                {
                    Assert.Catch<TransportException>(
                        () => Server.DefaultServerTransport.Listen(serverEndpoint,
                                                                   ServerConnectionOptions,
                                                                   Logger).Listener!.Dispose());
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
        public async Task AcceptNetworkSocketConnection_AcceptAsync_OperationCanceledExceptionAsync()
        {
            using IListener listener = CreateListener();

            using NetworkSocket clientConnection = CreateClientConnection();
            ValueTask<Endpoint> connectTask = clientConnection.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);

            using NetworkSocket serverConnection = await CreateServerConnectionAsync(listener);

            using var source = new CancellationTokenSource();
            source.Cancel();
            ValueTask<Endpoint?> acceptTask = serverConnection.AcceptAsync(
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

        private NetworkSocket CreateClientConnection() =>
           (Connection.DefaultClientTransport.CreateConnection(
                ClientEndpoint,
                ClientConnectionOptions,
                Logger) as NetworkSocketConnection)!.NetworkSocket;

        private static async ValueTask<NetworkSocket> CreateServerConnectionAsync(IListener listener) =>
            (await listener.AcceptAsync() as NetworkSocketConnection)!.NetworkSocket;
    }
}
