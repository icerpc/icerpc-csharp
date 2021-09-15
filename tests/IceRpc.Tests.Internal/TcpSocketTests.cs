// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System.Net.Sockets;

namespace IceRpc.Tests.Internal
{
    [TestFixture("tcp", false, AddressFamily.InterNetwork)]
    [TestFixture("tcp", true, AddressFamily.InterNetwork)]
    [TestFixture("tcp", false, AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class TcpTests : NetworkSocketBaseTest
    {
        private INetworkSocket ClientSocket => _clientSocket!;
        private INetworkSocket ServerSocket => _serverSocket!;
        private NetworkSocket? _clientSocket;
        private NetworkSocket? _serverSocket;

        public TcpTests(string transport, bool tls, AddressFamily addressFamily)
            : base(transport, tls, addressFamily)
        {
        }

        [SetUp]
        public async Task SetupAsync()
        {
            Task<NetworkSocket> acceptTask = AcceptAsync();
            Task<NetworkSocket> connectTask = ConnectAsync();
            _serverSocket = await acceptTask;
            _clientSocket = await connectTask;
        }

        [TearDown]
        public void TearDown()
        {
            _clientSocket?.Dispose();
            _serverSocket?.Dispose();
        }

        [Test]
        public void TcpSocket_ReceiveAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            ValueTask<int> receiveTask = ClientSocket.ReceiveAsync(new byte[1], canceled.Token);
            Assert.That(receiveTask.IsCompleted, Is.False);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        }

        [Test]
        public void TcpSocket_ReceiveAsync_ConnectionLostException()
        {
            _serverSocket!.Dispose();
            Assert.CatchAsync<ConnectionLostException>(
                async () => await ClientSocket.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void TcpSocket_ReceiveAsync_Dispose()
        {
            _clientSocket!.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void TcpSocket_ReceiveAsync_Exception()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await ClientSocket.ReceiveAsync(Array.Empty<byte>(), default));

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientSocket.ReceiveAsync(new byte[1], canceled.Token));
        }

        [Test]
        public async Task TcpSocket_SendAsync_CancellationAsync()
        {
            ServerSocket.Socket!.ReceiveBufferSize = 4096;
            ClientSocket.Socket!.SendBufferSize = 4096;

            // On some platforms the setting of the buffer sizes might not be granted, we make sure the buffers
            // are at least not larger than 16KB. The test below relies on the SendAsync to block when the connection
            // send/receive buffers fill up.
            Assert.That(ServerSocket.Socket!.ReceiveBufferSize, Is.LessThan(16 * 1024));
            Assert.That(ClientSocket.Socket!.SendBufferSize, Is.LessThan(16 * 1024));

            using var canceled = new CancellationTokenSource();

            // Wait for the SendAsync call to block.
            Task sendTask;
            do
            {
                sendTask = ClientSocket.SendAsync(OneMBSendBuffer, canceled.Token).AsTask();
                await Task.WhenAny(Task.Delay(500), sendTask);
            }
            while (sendTask.IsCompleted);

            // Cancel the blocked SendAsync and ensure OperationCanceledException is raised.
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await sendTask);
        }

        [Test]
        public void TcpSocket_SendAsync_ConnectionLostException()
        {
            _serverSocket!.Dispose();
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
        public void TcpSocket_SendAsync_Dispose()
        {
            _clientSocket!.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.SendAsync(OneBSendBuffer, default));
        }

        [Test]
        public void TcpSocket_SendAsync_Exception()
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
        public async Task TcpSocket_SendReceiveAsync(int size)
        {
            byte[] sendBuffer = new byte[size];

            ValueTask test1 = Test(ClientSocket, ServerSocket);
            ValueTask test2 = Test(ServerSocket, ClientSocket);

            await test1;
            await test2;

            async ValueTask Test(INetworkSocket connection1, INetworkSocket connection2)
            {
                ValueTask sendTask = connection1.SendAsync(sendBuffer, default);
                Memory<byte> receiveBuffer = new byte[size];
                int offset = 0;
                while (offset < size)
                {
                    offset += await connection2.ReceiveAsync(receiveBuffer[offset..], default);
                }
            }
        }
    }
}
