// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Net;
using System.Net.Sockets;

namespace IceRpc.Tests.Internal
{
    [TestFixture(false, AddressFamily.InterNetwork)]
    [TestFixture(true, AddressFamily.InterNetwork)]
    [TestFixture(null, AddressFamily.InterNetwork)]
    [TestFixture(false, AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class TcpConnectAcceptSocketTests : NetworkSocketBaseTest
    {
        private readonly bool? _tls;

        public TcpConnectAcceptSocketTests(bool? tls, AddressFamily addressFamily)
            : base("tcp", tls, addressFamily) =>
            _tls = tls;

        [Test]
        public async Task TcpConnectAcceptSocket_Listener_AcceptAsync()
        {
            using IListener listener = CreateListener();
            ValueTask<NetworkSocket> acceptTask = CreateServerNetworkSocketAsync(listener);

            using NetworkSocket clientSocket = CreateClientNetworkSocket();
            ValueTask<Endpoint> connectTask = clientSocket.ConnectAsync(ClientEndpoint, default);
            using NetworkSocket serverSocket = await acceptTask;
        }

        [Test]
        public void TcpConnectAcceptSocket_Listener_Constructor_TransportException()
        {
            using IListener listener = CreateListener();
            Assert.Throws<TransportException>(() => CreateListener());
        }

        [Test]
        public async Task TcpConnectAcceptSocket_AcceptAsync()
        {
            using IListener listener = CreateListener();
            ValueTask<NetworkSocket> acceptTask = CreateServerNetworkSocketAsync(listener);

            using NetworkSocket clientSocket = CreateClientNetworkSocket();
            ValueTask<Endpoint> connectTask = clientSocket.ConnectAsync(ClientEndpoint, default);

            using NetworkSocket serverSocket = await acceptTask;
            ValueTask<Endpoint> acceptTask2 = serverSocket.ConnectAsync(ServerEndpoint, default);

            await connectTask;

            if (_tls == null)
            {
                await clientSocket.SendAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
            }

            await acceptTask2;

            Assert.That(serverSocket, Is.InstanceOf<TcpSocket>());
        }

        [Test]
        public async Task TcpConnectAcceptSocket_AcceptAsync_ConnectionLostExceptionAsync()
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
            if (_tls == false)
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
        public void TcpConnectAcceptSocket_Listener_AddressReuse(bool wildcard1, bool wildcard2)
        {
            IListener listener;
            if (wildcard1)
            {
                Endpoint serverEndpoint = ServerEndpoint with { Host = "::0" };
                IServerTransport serverTransport = TestHelper.CreateServerTransport(serverEndpoint);
                listener = serverTransport.Listen(serverEndpoint);
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
                    Assert.DoesNotThrow(() => serverTransport.Listen(serverEndpoint).Dispose());
                }
                else
                {
                    Assert.Catch<TransportException>(() => serverTransport.Listen(serverEndpoint).Dispose());
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
        public async Task TcpConnectAcceptSocket_AcceptAsync_OperationCanceledExceptionAsync()
        {
            using IListener listener = CreateListener();

            using NetworkSocket clientSocket = CreateClientNetworkSocket();
            ValueTask<Endpoint> connectTask = clientSocket.ConnectAsync(ClientEndpoint, default);

            using NetworkSocket serverSocket = await CreateServerNetworkSocketAsync(listener);

            using var source = new CancellationTokenSource();
            source.Cancel();
            ValueTask<Endpoint> acceptTask = serverSocket.ConnectAsync(ServerEndpoint, source.Token);

            if (_tls == false)
            {
                // Server-side ConnectionAsync is a no-op for non-secure TCP connections so it won't throw.
                await acceptTask;
            }
            else
            {
                Assert.CatchAsync<OperationCanceledException>(async () => await acceptTask);
            }
        }

        [Test]
        public void TcpConnectAcceptSocket_ConnectAsync_ConnectionRefusedException()
        {
            using NetworkSocket clientSocket = CreateClientNetworkSocket();
            Assert.ThrowsAsync<ConnectionRefusedException>(
                async () => await clientSocket.ConnectAsync(ClientEndpoint, default));
        }

        [Test]
        public void TcpConnectAcceptSocket_ConnectAsync_OperationCanceledException()
        {
            using IListener listener = CreateListener();

            using var source = new CancellationTokenSource();
            if (_tls == false)
            {
                // ConnectAsync might complete synchronously with TCP
            }
            else
            {
                using NetworkSocket clientSocket = CreateClientNetworkSocket();
                ValueTask<Endpoint> connectTask = clientSocket.ConnectAsync(ClientEndpoint, source.Token);
                source.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await connectTask);
            }

            using var source2 = new CancellationTokenSource();
            source2.Cancel();
            using NetworkSocket clientSocket2 = CreateClientNetworkSocket();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await clientSocket2.ConnectAsync(ClientEndpoint, source2.Token));
        }

        private static async ValueTask<NetworkSocket> CreateServerNetworkSocketAsync(IListener listener) =>
            GetNetworkSocket(await listener.AcceptAsync());
    }
}
