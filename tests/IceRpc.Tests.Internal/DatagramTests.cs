// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [TestFixture(AddressFamily.InterNetwork)]
    [TestFixture(AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class DatagramTests : SingleStreamSocketBaseTest
    {
        public DatagramTests(AddressFamily addressFamily)
            : base(Protocol.Ice1, "udp", NonSecure.Always, addressFamily)
        {
        }

        [TestCase(1, 1)]
        [TestCase(1, 1024)]
        [TestCase(1, 4096)]
        [TestCase(2, 1024)]
        [TestCase(10, 1024)]
        public async Task DatagramSocket_MultipleSendReceiveAsync(int clientSocketCount, int size)
        {
            var sendBuffer = new List<ArraySegment<byte>>() { new byte[size] };
            new Random().NextBytes(sendBuffer[0]);

            List<SingleStreamSocket> clientSockets = new();
            clientSockets.Add(ClientSocket);
            for (int i = 0; i < clientSocketCount; ++i)
            {
                clientSockets.Add(await SingleStreamSocketAsync(ConnectAsync()));
            }

            // Datagrams aren't reliable, try up to 5 times in case the datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    foreach (SingleStreamSocket socket in clientSockets)
                    {
                        using var source = new CancellationTokenSource(1000);
                        ValueTask<int> sendTask = socket.SendDatagramAsync(sendBuffer, default);

                        ArraySegment<byte> receiveBuffer = await ServerSocket.ReceiveDatagramAsync(source.Token);
                        Assert.AreEqual(await sendTask, receiveBuffer.Count);
                        Assert.AreEqual(sendBuffer[0], receiveBuffer);
                    }
                    break;
                }
                catch (OperationCanceledException)
                {
                }
            }
            Assert.AreNotEqual(0, count);

            count = 5;
            while (count-- > 0)
            {
                try
                {
                    foreach (SingleStreamSocket socket in clientSockets)
                    {
                        await socket.SendDatagramAsync(sendBuffer, default);
                    }
                    foreach (SingleStreamSocket socket in clientSockets)
                    {
                        using var source = new CancellationTokenSource(1000);
                        ArraySegment<byte> receiveBuffer = await ServerSocket.ReceiveDatagramAsync(source.Token);
                        Assert.AreEqual(sendBuffer[0].Count, receiveBuffer.Count);
                    }
                    break;
                }
                catch (OperationCanceledException)
                {
                }
            }
            Assert.AreNotEqual(0, count);
        }

        [Test]
        public void DatagramSocket_ReceiveDatagramAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            ValueTask<ArraySegment<byte>> receiveTask = ClientSocket.ReceiveDatagramAsync(canceled.Token);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        }

        [Test]
        public void DatagramSocket_ReceiveDatagramAsync_Dispose()
        {
            ClientSocket.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.ReceiveDatagramAsync(default));
        }

        [Test]
        public void DatagramSocket_ReceiveAsync_Exception()
        {
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await ClientSocket.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void DatagramSocket_SendAsync_Exception()
        {
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await ClientSocket.SendAsync(OneBSendBuffer, default));
        }

        [Test]
        public void DatagramSocket_SendDatagramAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var buffer = new List<ArraySegment<byte>>() { new byte[1] };
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientSocket.SendDatagramAsync(buffer, canceled.Token));
        }

        [Test]
        public void DatagramSocket_SendDatagramAsync_Dispose()
        {
            ClientSocket.Dispose();
            Assert.CatchAsync<TransportException>(
                async () => await ClientSocket.SendDatagramAsync(OneBSendBuffer, default));
        }

        [Test]
        public void DatagramSocket_SendDatagramAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientSocket.SendDatagramAsync(OneBSendBuffer, canceled.Token));
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(4096)]
        public async Task DatagramSocket_SendReceiveAsync(int size)
        {
            var sendBuffer = new List<ArraySegment<byte>>() { new byte[size] };
            new Random().NextBytes(sendBuffer[0]);

            // Datagrams aren't reliable, try up to 5 times in case the datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    using var source = new CancellationTokenSource(1000);
                    ValueTask<int> sendTask = ClientSocket.SendDatagramAsync(sendBuffer, default);
                    ArraySegment<byte> receiveBuffer = await ServerSocket.ReceiveDatagramAsync(source.Token);
                    Assert.AreEqual(await sendTask, receiveBuffer.Count);
                    Assert.AreEqual(sendBuffer[0], receiveBuffer);
                    break;
                }
                catch (OperationCanceledException)
                {
                }
            }
            Assert.AreNotEqual(0, count);
        }
    }
}
