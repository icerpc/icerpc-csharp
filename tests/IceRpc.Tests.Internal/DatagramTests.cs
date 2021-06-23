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
    [TestFixture(AddressFamily.InterNetwork)]
    [TestFixture(AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class DatagramTests : NetworkSocketConnectionBaseTest
    {
        public DatagramTests(AddressFamily addressFamily)
            : base(Protocol.Ice1, "udp", tls: false, addressFamily)
        {
        }

        [TestCase(1, 1)]
        [TestCase(1, 1024)]
        [TestCase(1, 4096)]
        [TestCase(2, 1024)]
        [TestCase(10, 1024)]
        public async Task Datagram_MultipleSendReceiveAsync(int outgoingConnectionCount, int size)
        {
            var sendBuffer = new byte[size];
            new Random().NextBytes(sendBuffer);

            List<NetworkSocket> outgoingConnections = new();
            outgoingConnections.Add(OutgoingConnection);
            for (int i = 0; i < outgoingConnectionCount; ++i)
            {
                outgoingConnections.Add(await NetworkSocketConnectionAsync(ConnectAsync()));
            }

            // Datagrams aren't reliable, try up to 5 times in case the datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    foreach (NetworkSocket connection in outgoingConnections)
                    {
                        using var source = new CancellationTokenSource(1000);
                        ValueTask sendTask = connection.SendAsync(sendBuffer, default);

                        Memory<byte> receiveBuffer = new byte[IncomingConnection.DatagramMaxReceiveSize];
                        int received = await IncomingConnection.ReceiveAsync(receiveBuffer, source.Token);

                        Assert.AreEqual(sendBuffer.Length, received);
                        for (int i = 0; i < received; ++i)
                        {
                            Assert.AreEqual(sendBuffer[i], receiveBuffer.Span[i]);
                        }
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
                    foreach (NetworkSocket connection in outgoingConnections)
                    {
                        await connection.SendAsync(sendBuffer, default);
                    }
                    foreach (NetworkSocket connection in outgoingConnections)
                    {
                        using var source = new CancellationTokenSource(1000);
                        Memory<byte> receiveBuffer = new byte[IncomingConnection.DatagramMaxReceiveSize];
                        int received = await IncomingConnection.ReceiveAsync(receiveBuffer, source.Token);
                        Assert.AreEqual(sendBuffer.Length, received);
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
        public void Datagram_ReceiveAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            Memory<byte> receiveBuffer = new byte[OutgoingConnection.DatagramMaxReceiveSize];
            ValueTask<int> receiveTask = OutgoingConnection.ReceiveAsync(receiveBuffer, canceled.Token);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        }

        [Test]
        public void Datagram_ReceiveAsync_Dispose()
        {
            OutgoingConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () =>
                await OutgoingConnection.ReceiveAsync(new byte[256], default));
        }

        [Test]
        public void Datagram_SendAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var buffer = new byte[1];
            Assert.CatchAsync<OperationCanceledException>(
                async () => await OutgoingConnection.SendAsync(buffer, canceled.Token));
        }

        [Test]
        public void Datagram_SendAsync_Dispose()
        {
            OutgoingConnection.Dispose();
            Assert.CatchAsync<TransportException>(
                async () => await OutgoingConnection.SendAsync(OneBSendBuffer, default));
        }

        [Test]
        public void Datagram_SendAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await OutgoingConnection.SendAsync(OneBSendBuffer, canceled.Token));
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(4096)]
        public async Task Datagram_SendReceiveAsync(int size)
        {
            var sendBuffer = new byte[size];
            new Random().NextBytes(sendBuffer);

            // Datagrams aren't reliable, try up to 5 times in case the datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    using var source = new CancellationTokenSource(1000);
                    ValueTask sendTask = OutgoingConnection.SendAsync(sendBuffer, default);
                    Memory<byte> receiveBuffer = new byte[IncomingConnection.DatagramMaxReceiveSize];
                    int received = await IncomingConnection.ReceiveAsync(receiveBuffer, source.Token);
                    Assert.AreEqual(sendBuffer.Length, received);
                    for (int i = 0; i < received; ++i)
                    {
                        Assert.AreEqual(sendBuffer[i], receiveBuffer.Span[i]);
                    }
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
