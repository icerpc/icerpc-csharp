// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Ice;

namespace IceRpc.Tests.Internal
{
    public class SingleStreamSocketBaseTest : SocketBaseTest
    {
        protected static readonly List<ArraySegment<byte>> OneBSendBuffer = new() { new byte[1] };
        protected static readonly List<ArraySegment<byte>> OneMBSendBuffer = new() { new byte[1024 * 1024] };
        protected SingleStreamSocket ClientSocket => _clientSocket!;
        protected SingleStreamSocket ServerSocket => _serverSocket!;
        private SingleStreamSocket? _clientSocket;
        private SingleStreamSocket? _serverSocket;

        public SingleStreamSocketBaseTest(Protocol protocol, string transport, bool secure)
            : base(protocol, transport, secure)
        {
        }

        [SetUp]
        public async Task SetUp()
        {
            ValueTask<SingleStreamSocket> connectTask = SingleStreamSocket(ConnectAsync());
            ValueTask<SingleStreamSocket> acceptTask = SingleStreamSocket(AcceptAsync());

            _clientSocket = await connectTask;
            _serverSocket = await acceptTask;

            static async ValueTask<SingleStreamSocket> SingleStreamSocket(Task<MultiStreamSocket> socket) =>
                (await socket as MultiStreamOverSingleStreamSocket)!.Underlying;
        }

        [TearDown]
        public void TearDown()
        {
            _clientSocket?.Dispose();
            _serverSocket?.Dispose();
        }
    }

    [Parallelizable(scope: ParallelScope.Fixtures)]
    [TestFixture(Protocol.Ice2, "tcp", false)]
    [TestFixture(Protocol.Ice2, "ws", false)]
    [TestFixture(Protocol.Ice2, "tcp", true)]
    [TestFixture(Protocol.Ice2, "ws", true)]
    [TestFixture(Protocol.Ice1, "tcp", false)]
    [TestFixture(Protocol.Ice1, "ssl", true)]
    public class SingleStreamSocketTests : SingleStreamSocketBaseTest
    {
        public SingleStreamSocketTests(Protocol protocol, string transport, bool secure)
            : base(protocol, transport, secure)
        {
        }

        [Test]
        public async Task SingleStreamSocket_CloseAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                // This will either complete successfully or with an OperationCanceledException
                await ClientSocket.CloseAsync(new InvalidDataException(""), canceled.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        [Test]
        public void SingleStreamSocket_Dispose()
        {
            ClientSocket.Dispose();
            ServerSocket.Dispose();
            ClientSocket.Dispose();
            ServerSocket.Dispose();
        }

        [Test]
        public void SingleStreamSocket_Properties()
        {
            Test(ClientSocket);
            Test(ServerSocket);

            void Test(SingleStreamSocket socket)
            {
                Assert.NotNull(socket.Socket);
                Assert.AreEqual(socket.SslStream != null, IsSecure);
                Assert.IsNotEmpty(socket.ToString());
            }
        }

        [Test]
        public void SingleStreamSocket_ReceiveAsync_Cancelation()
        {
            using var canceled = new CancellationTokenSource();
            ValueTask<int> receiveTask = ClientSocket.ReceiveAsync(new byte[1], canceled.Token);
            Assert.IsFalse(receiveTask.IsCompleted);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        }

        [Test]
        public void SingleStreamSocket_ReceiveAsync_ConnectionLostException()
        {
            ServerSocket.Dispose();
            Assert.CatchAsync<ConnectionLostException>(
                async () => await ClientSocket.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void SingleStreamSocket_ReceiveAsync_Dispose()
        {
            ClientSocket.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void SingleStreamSocket_ReceiveAsync_Exception()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await ClientSocket.ReceiveAsync(Array.Empty<byte>(), default));

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientSocket.ReceiveAsync(new byte[1], canceled.Token));
        }

        [Test]
        public void SingleStreamSocket_ReceiveDatagramAsync_Exception()
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await ClientSocket.ReceiveDatagramAsync(default));
        }

        [Test]
        public void SingleStreamSocket_SendAsync_Cancelation()
        {
            ServerSocket.Socket!.ReceiveBufferSize = 4096;
            ClientSocket.Socket!.SendBufferSize = 4096;

            // On some platforms the setting of the buffer sizes might not be granted, we make sure the buffers
            // are at least not larger than 16KB. The test below relies on the SendAsync to block when the socket
            // send/receive buffers fill up.
            Assert.Less(ServerSocket.Socket!.ReceiveBufferSize, 16 * 1024);
            Assert.Less(ClientSocket.Socket!.SendBufferSize, 16 * 1024);

            using var canceled = new CancellationTokenSource();

            // Wait for the SendAsync call to block.
            ValueTask<int> sendTask;
            do
            {
                sendTask = ClientSocket.SendAsync(OneMBSendBuffer, canceled.Token);
            }
            while (sendTask.IsCompleted);
            sendTask = ClientSocket.SendAsync(OneMBSendBuffer, canceled.Token);
            Assert.IsFalse(sendTask.IsCompleted);

            // Cancel the blocked SendAsync and ensure OperationCanceledException is raised.
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await sendTask);
        }

        [Test]
        public void SingleStreamSocket_SendAsync_ConnectionLostException()
        {
            ServerSocket.Dispose();
            Assert.CatchAsync<ConnectionLostException>(
                async () =>
                {
                    while (true)
                    {
                        await ClientSocket.SendAsync(OneMBSendBuffer, default);
                    }
                });
        }

        [Test]
        public void SingleStreamSocket_SendAsync_Dispose()
        {
            ClientSocket.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.SendAsync(OneBSendBuffer, default));
        }

        [Test]
        public void SingleStreamSocket_SendAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientSocket.SendAsync(OneBSendBuffer, canceled.Token));
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(16 * 1024)]
        [TestCase(512 * 1024)]
        public async Task SingleStreamSocket_SendReceiveAsync(int size)
        {
            var sendBuffer = new List<ArraySegment<byte>>() { new byte[size] };

            ValueTask test1 = Test(ClientSocket, ServerSocket);
            ValueTask test2 = Test(ServerSocket, ClientSocket);

            await test1;
            await test2;

            async ValueTask Test(SingleStreamSocket socket1, SingleStreamSocket socket2)
            {
                ValueTask<int> sendTask = socket1.SendAsync(sendBuffer, default);
                ArraySegment<byte> receiveBuffer = new byte[size];
                int offset = 0;
                while (offset < size)
                {
                    offset += await socket2.ReceiveAsync(receiveBuffer.Slice(offset), default);
                }
                Assert.AreEqual(await sendTask, size);
            }
        }
    }

    [TestFixture("ws", false)]
    [TestFixture("ws", true)]
    public class WSSocketTests : SingleStreamSocketBaseTest
    {
        public WSSocketTests(string transport, bool secure)
            : base(Protocol.Ice2, transport, secure)
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

    [Parallelizable(scope: ParallelScope.Fixtures)]
    [TestFixture(Protocol.Ice1, "tcp", false)]
    [TestFixture(Protocol.Ice1, "ssl", true)]
    [TestFixture(Protocol.Ice2, "tcp", false)]
    [TestFixture(Protocol.Ice2, "tcp", true)]
    [TestFixture(Protocol.Ice2, "ws", false)]
    [TestFixture(Protocol.Ice2, "ws", true)]
    [Timeout(5000)]
    public class AcceptSingleStreamSocketTests : SocketBaseTest
    {
        public AcceptSingleStreamSocketTests(Protocol protocol, string transport, bool secure)
            : base(protocol, transport, secure)
        {
        }

        [Test]
        public async Task AcceptSingleStreamSocket_Acceptor_AcceptAsync()
        {
            using IAcceptor acceptor = await CreateAcceptorAsync();
            ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);
            using SingleStreamSocket clientSocket = await CreateClientSocketAsync();
            ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(ClientEndpoint, IsSecure, default);
            using SingleStreamSocket serverSocket = await acceptTask;
        }

        [Test]
        public async Task AcceptSingleStreamSocket_Acceptor_Constructor_TransportException()
        {
            using IAcceptor acceptor = await CreateAcceptorAsync();
            Assert.ThrowsAsync<TransportException>(async () => await CreateAcceptorAsync());
        }

        public async Task AcceptSingleStreamSocket_AcceptAsync()
        {
            using IAcceptor acceptor = await CreateAcceptorAsync();
            ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);

            using SingleStreamSocket clientSocket = await CreateClientSocketAsync();
            ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(ClientEndpoint, IsSecure, default);

            using SingleStreamSocket serverSocket = await acceptTask;

            SingleStreamSocket socket = await serverSocket.AcceptAsync(ServerEndpoint, default);
            await connectTask;

            // The SslSocket is returned if a secure connection is requested.
            Assert.IsTrue(IsSecure ? socket != serverSocket : socket == serverSocket);
        }

        [Test]
        public async Task AcceptSingleStreamSocket_AcceptAsync_ConnectionLostException()
        {
            using IAcceptor acceptor = await CreateAcceptorAsync();
            ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);

            SingleStreamSocket clientSocket = await CreateClientSocketAsync();
            ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(ClientEndpoint, IsSecure, default);

            using SingleStreamSocket serverSocket = await acceptTask;

            clientSocket.Dispose();

            AsyncTestDelegate testDelegate;
            if (!IsSecure && ClientEndpoint.Protocol == Protocol.Ice1 && TransportName == "tcp")
            {
                // AcceptAsync is a no-op for Ice1 non-secure TCP connections so it won't throw.
                await serverSocket.AcceptAsync(ServerEndpoint, default);
                testDelegate = async () => await serverSocket.ReceiveAsync(new byte[1], default);
            }
            else
            {
                testDelegate = async () => await serverSocket.AcceptAsync(ServerEndpoint, default);
            }
            Assert.ThrowsAsync<ConnectionLostException>(testDelegate);
        }

        [Test]
        public async Task AcceptSingleStreamSocket_AcceptAsync_OperationCanceledException()
        {
            using IAcceptor acceptor = await CreateAcceptorAsync();

            using SingleStreamSocket clientSocket = await CreateClientSocketAsync();
            ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(ClientEndpoint, IsSecure, default);

            using SingleStreamSocket serverSocket = await CreateServerSocketAsync(acceptor);

            using var source = new CancellationTokenSource();
            source.Cancel();
            ValueTask<SingleStreamSocket> acceptTask = serverSocket.AcceptAsync(ServerEndpoint, source.Token);

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

        private async ValueTask<SingleStreamSocket> CreateClientSocketAsync()
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(ClientEndpoint.Host).ConfigureAwait(false);
            Connection connection =
                (ClientEndpoint as IPEndpoint)!.CreateConnection(
                    new IPEndPoint(addresses[0], ClientEndpoint.Port), null, default);
            return (connection.Socket as MultiStreamOverSingleStreamSocket)!.Underlying;
        }

        private static async ValueTask<SingleStreamSocket> CreateServerSocketAsync(IAcceptor acceptor)
        {
            MultiStreamSocket multiStreamServerSocket = (await acceptor.AcceptAsync()).Socket;
            return (multiStreamServerSocket as MultiStreamOverSingleStreamSocket)!.Underlying;
        }
    }

    [Parallelizable(scope: ParallelScope.Fixtures)]
    [TestFixture(Protocol.Ice1, "tcp", false)]
    [TestFixture(Protocol.Ice1, "ssl", true)]
    [TestFixture(Protocol.Ice2, "tcp", false)]
    [TestFixture(Protocol.Ice2, "tcp", true)]
    [TestFixture(Protocol.Ice2, "ws", false)]
    [TestFixture(Protocol.Ice2, "ws", true)]
    [Timeout(5000)]
    public class ConnectSingleStreamSocketTests : SocketBaseTest
    {
        public ConnectSingleStreamSocketTests(Protocol protocol, string transport, bool secure)
            : base(protocol, transport, secure)
        {
        }

        [Test]
        public async Task ConnectSingleStreamSocket_ConnectAsync_ConnectionRefusedException()
        {
            using SingleStreamSocket clientSocket = await CreateClientSocketAsync();
            Assert.ThrowsAsync<ConnectionRefusedException>(
                async () => await clientSocket.ConnectAsync(ClientEndpoint, IsSecure, default));
        }

        public async Task ConnectSingleStreamSocket_ConnectAsync_OperationCanceledException()
        {
            using IAcceptor acceptor = await CreateAcceptorAsync();

            using var source = new CancellationTokenSource();

            using SingleStreamSocket clientSocket = await CreateClientSocketAsync();
            ValueTask<SingleStreamSocket> connectTask =
                clientSocket.ConnectAsync(ClientEndpoint, IsSecure, source.Token);
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await connectTask);

            using SingleStreamSocket clientSocket2 = await CreateClientSocketAsync();
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await clientSocket2.ConnectAsync(ClientEndpoint, IsSecure, source.Token));
        }

        private async ValueTask<SingleStreamSocket> CreateClientSocketAsync()
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(ClientEndpoint.Host).ConfigureAwait(false);
            Connection connection =
                (ClientEndpoint as IPEndpoint)!.CreateConnection(
                    new IPEndPoint(addresses[0], ClientEndpoint.Port), null, default);
            return (connection.Socket as MultiStreamOverSingleStreamSocket)!.Underlying;
        }

        private static async ValueTask<SingleStreamSocket> CreateServerSocketAsync(IAcceptor acceptor)
        {
            MultiStreamSocket multiStreamServerSocket = (await acceptor.AcceptAsync()).Socket;
            return (multiStreamServerSocket as MultiStreamOverSingleStreamSocket)!.Underlying;
        }
    }
}
