// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
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

        // Using the Ice1 protocol is important to workaround the Ice2 one-byte peek check done in
        // AcceptAsync to figure out if it's a secure connection or not.
        public SingleStreamSocketBaseTest(string transport, bool secure)
            : base(Protocol.Ice1, transport, secure)
        {
        }

        [SetUp]
        public async Task SetUp()
        {
            ValueTask<SingleStreamSocket> clientInitialize = SingleStreamSocket(ConnectAsync());
            _serverSocket = await SingleStreamSocket(AcceptAsync());
            _clientSocket = await clientInitialize;

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

    // TODO: investigate why ParallelSocket.All is causing SSL failures
    [Parallelizable(scope: ParallelScope.Fixtures)]
    [TestFixture("tcp", false)]
    [TestFixture("ws", false)]
    [TestFixture("ssl", true)]
    [TestFixture("wss", true)]
    public class SingleStreamSocketTests : SingleStreamSocketBaseTest
    {
        public SingleStreamSocketTests(string transport, bool secure)
            : base(transport, secure)
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
    [TestFixture("wss", true)]
    public class WSSocketTests : SingleStreamSocketBaseTest
    {
        public WSSocketTests(string transport, bool secure)
            : base(transport, secure)
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
}
