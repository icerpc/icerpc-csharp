// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Net;
using System.Net.Sockets;

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
    public class AcceptSocketConnectionTests : ConnectionBaseTest
    {
        public AcceptSocketConnectionTests(
            Protocol protocol,
            string transport,
            bool tls,
            AddressFamily addressFamily)
            : base(protocol, transport, tls, addressFamily)
        {
        }

        [Test]
        public async Task AcceptSocketConnection_Listener_AcceptAsync()
        {
            using IListener listener = CreateListener();
            ValueTask<NetworkSocket> acceptTask = CreateServerNetworkSocketAsync(listener);

            using NetworkSocket clientSocket = CreateClientNetworkSocket();
            ValueTask<Endpoint> connectTask = clientSocket.ConnectAsync(ClientEndpoint, default);
            using NetworkSocket serverSocket = await acceptTask;
        }

        [Test]
        public void AcceptSocketConnection_Listener_Constructor_TransportException()
        {
            using IListener listener = CreateListener();
            Assert.Throws<TransportException>(() => CreateListener());
        }

        [Test]
        public async Task AcceptSocketConnection_AcceptAsync()
        {
            using IListener listener = CreateListener();
            ValueTask<NetworkSocket> acceptTask = CreateServerNetworkSocketAsync(listener);

            using NetworkSocket clientSocket = CreateClientNetworkSocket();
            ValueTask<Endpoint> connectTask = clientSocket.ConnectAsync(ClientEndpoint, default);

            using NetworkSocket serverSocket = await acceptTask;
            ValueTask<Endpoint> acceptTask2 = serverSocket.ConnectAsync(ServerEndpoint, default);

            await connectTask;

            if (ClientEndpoint.Protocol == Protocol.Ice2 && TransportName == "tcp")
            {
                await clientSocket.SendAsync(new byte[1], default);
            }

            await acceptTask2;

            Assert.That(serverSocket, Is.InstanceOf<TcpSocket>());
        }

        [Test]
        public async Task AcceptSocketConnection_AcceptAsync_ConnectionLostExceptionAsync()
        {
            using IListener listener = CreateListener();
            ValueTask<NetworkSocket> acceptTask = CreateServerNetworkSocketAsync(listener);

            NetworkSocket clientSocket = CreateClientNetworkSocket();

            // We don't use clientSocket.ConnectAsync() here as this would start the TLS handshake for secure
            // connections
            await clientSocket.Socket!.ConnectAsync(
                new DnsEndPoint(ClientEndpoint.Host, ClientEndpoint.Port)).ConfigureAwait(false);

            using NetworkSocket serverSocket = await acceptTask;

            clientSocket.Dispose();

            AsyncTestDelegate testDelegate;
            if (!IsSecure && TransportName == "tcp")
            {
                // Server side ConnectAsync is a no-op for non secure TCP connections so it won't throw.
                await serverSocket.ConnectAsync(ServerEndpoint, default);
                testDelegate = async () => await serverSocket.ReceiveAsync(new byte[1], default);
            }
            else
            {
                testDelegate = async () => await serverSocket.ConnectAsync(ServerEndpoint, default);
            }
            Assert.ThrowsAsync<ConnectionLostException>(testDelegate);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void AcceptSocketConnection_Listener_AddressReuse(bool wildcard1, bool wildcard2)
        {
            IListener listener;
            if (wildcard1)
            {
                Endpoint serverEndpoint = ServerEndpoint with { Host = "::0" };
                IServerTransport serverTransport = TestHelper.CreateServerTransport(serverEndpoint);
                listener = serverTransport.Listen(
                    serverEndpoint,
                    LogAttributeLoggerFactory.Instance).Listener!;
            }
            else
            {
                listener = CreateListener();
            }

            if (wildcard2)
            {
                Endpoint serverEndpoint = ServerEndpoint with { Host = "::0" };
                IServerTransport serverTransport = TestHelper.CreateServerTransport(serverEndpoint);
                if (OperatingSystem.IsMacOS())
                {
                    // On macOS, it's still possible to bind to a specific address even if a connection is bound
                    // to the wildcard address.
                    Assert.DoesNotThrow(
                        () => serverTransport.Listen(
                            serverEndpoint,
                            LogAttributeLoggerFactory.Instance).Listener!.Dispose());
                }
                else
                {
                    Assert.Catch<TransportException>(
                        () => serverTransport.Listen(
                            serverEndpoint,
                            LogAttributeLoggerFactory.Instance).Listener!.Dispose());
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
        public async Task AcceptSocketConnection_AcceptAsync_OperationCanceledExceptionAsync()
        {
            using IListener listener = CreateListener();

            using NetworkSocket clientSocket = CreateClientNetworkSocket();
            ValueTask<Endpoint> connectTask = clientSocket.ConnectAsync(ClientEndpoint, default);

            using NetworkSocket serverSocket = await CreateServerNetworkSocketAsync(listener);

            using var source = new CancellationTokenSource();
            source.Cancel();
            ValueTask<Endpoint> acceptTask = serverSocket.ConnectAsync(ServerEndpoint, source.Token);

            if (!IsSecure && TransportName == "tcp")
            {
                // Server-side ConnectionAsync is a no-op for non-secure TCP connections so it won't throw.
                await acceptTask;
            }
            else
            {
                Assert.CatchAsync<OperationCanceledException>(async () => await acceptTask);
            }
        }

        private NetworkSocket CreateClientNetworkSocket() =>
            ((SocketConnection)CreateClientConnection()).NetworkSocket;

        private static async ValueTask<NetworkSocket> CreateServerNetworkSocketAsync(IListener listener) =>
            ((SocketConnection)await listener.AcceptAsync()).NetworkSocket;
    }
}
