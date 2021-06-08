// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [TestFixture("tcp", false, AddressFamily.InterNetwork)]
    [TestFixture("ws", false, AddressFamily.InterNetwork)]
    [TestFixture("tcp", true, AddressFamily.InterNetwork)]
    [TestFixture("ws", true, AddressFamily.InterNetwork)]
    [TestFixture("tcp", false, AddressFamily.InterNetworkV6)]
    [Timeout(30000)]
    public class NonDatagramTests : SingleStreamConnectionBaseTest
    {
        public NonDatagramTests(string transport, bool tls, AddressFamily addressFamily)
            : base(Protocol.Ice2, transport, tls, addressFamily)
        {
        }

        [Test]
        public void NonDatagramSocket_ReceiveAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            ValueTask<int> receiveTask = ClientSocket.ReceiveAsync(new byte[1], canceled.Token);
            Assert.IsFalse(receiveTask.IsCompleted);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        }

        [Test]
        public void NonDatagramSocket_ReceiveAsync_ConnectionLostException()
        {
            ServerSocket.Dispose();
            Assert.CatchAsync<ConnectionLostException>(
                async () => await ClientSocket.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void NonDatagramSocket_ReceiveAsync_Dispose()
        {
            ClientSocket.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void NonDatagramSocket_ReceiveAsync_Exception()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await ClientSocket.ReceiveAsync(Array.Empty<byte>(), default));

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientSocket.ReceiveAsync(new byte[1], canceled.Token));
        }

        [Test]
        public void NonDatagramSocket_ReceiveDatagramAsync_Exception()
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await ClientSocket.ReceiveDatagramAsync(default));
        }

        [Test]
        public async Task NonDatagramSocket_SendAsync_CancellationAsync()
        {
            ServerSocket.NetworkSocket!.ReceiveBufferSize = 4096;
            ClientSocket.NetworkSocket!.SendBufferSize = 4096;

            // On some platforms the setting of the buffer sizes might not be granted, we make sure the buffers
            // are at least not larger than 16KB. The test below relies on the SendAsync to block when the socket
            // send/receive buffers fill up.
            Assert.Less(ServerSocket.NetworkSocket!.ReceiveBufferSize, 16 * 1024);
            Assert.Less(ClientSocket.NetworkSocket!.SendBufferSize, 16 * 1024);

            using var canceled = new CancellationTokenSource();

            // Wait for the SendAsync call to block.
            Task<int> sendTask;
            do
            {
                sendTask = ClientSocket.SendAsync(OneMBSendBuffer, canceled.Token).AsTask();
                await Task.WhenAny(Task.Delay(500), sendTask);
            }
            while (sendTask.IsCompleted);
            sendTask = ClientSocket.SendAsync(OneMBSendBuffer, canceled.Token).AsTask();
            Assert.IsFalse(sendTask.IsCompleted);

            // Cancel the blocked SendAsync and ensure OperationCanceledException is raised.
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await sendTask);
        }

        [Test]
        public void NonDatagramSocket_SendAsync_ConnectionLostException()
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
        public void NonDatagramSocket_SendAsync_Dispose()
        {
            ClientSocket.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.SendAsync(OneBSendBuffer, default));
        }

        [Test]
        public void NonDatagramSocket_SendAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientSocket.SendAsync(OneBSendBuffer, canceled.Token));
        }

        [Test]
        public void NonDatagramSocket_SendDatagramAsync_Exception()
        {
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await ClientSocket.SendDatagramAsync(OneBSendBuffer, default));
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(16 * 1024)]
        [TestCase(512 * 1024)]
        public async Task NonDatagramSocket_SendReceiveAsync(int size)
        {
            var sendBuffer = new List<ArraySegment<byte>>() { new byte[size] };

            ValueTask test1 = Test(ClientSocket, ServerSocket);
            ValueTask test2 = Test(ServerSocket, ClientSocket);

            await test1;
            await test2;

            async ValueTask Test(SingleStreamConnection socket1, SingleStreamConnection socket2)
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
}
