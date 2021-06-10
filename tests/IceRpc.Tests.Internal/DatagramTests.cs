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
    public class DatagramTests : SingleStreamConnectionBaseTest
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

            List<SingleStreamConnection> outgoingConnections = new();
            outgoingConnections.Add(OutgoingConnection);
            for (int i = 0; i < outgoingConnectionCount; ++i)
            {
                outgoingConnections.Add(await SingleStreamConnectionAsync(ConnectAsync()));
            }

            // Datagrams aren't reliable, try up to 5 times in case the datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    foreach (SingleStreamConnection connection in outgoingConnections)
                    {
                        using var source = new CancellationTokenSource(1000);
                        ValueTask<int> sendTask = connection.SendDatagramAsync(sendBuffer, default);

                        ArraySegment<byte> receiveBuffer = await IncomingConnection.ReceiveDatagramAsync(source.Token);
                        Assert.AreEqual(await sendTask, receiveBuffer.Count);
                        Assert.AreEqual(sendBuffer, receiveBuffer);
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
                    foreach (SingleStreamConnection connection in outgoingConnections)
                    {
                        await connection.SendDatagramAsync(sendBuffer, default);
                    }
                    foreach (SingleStreamConnection connection in outgoingConnections)
                    {
                        using var source = new CancellationTokenSource(1000);
                        ArraySegment<byte> receiveBuffer = await IncomingConnection.ReceiveDatagramAsync(source.Token);
                        Assert.AreEqual(sendBuffer.Length, receiveBuffer.Count);
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
        public void Datagram_ReceiveDatagramAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            ValueTask<ArraySegment<byte>> receiveTask = OutgoingConnection.ReceiveDatagramAsync(canceled.Token);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        }

        [Test]
        public void Datagram_ReceiveDatagramAsync_Dispose()
        {
            OutgoingConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await OutgoingConnection.ReceiveDatagramAsync(default));
        }

        [Test]
        public void Datagram_ReceiveAsync_Exception()
        {
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await OutgoingConnection.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void Datagram_SendAsync_Exception()
        {
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await OutgoingConnection.SendAsync(OneBSendBuffer, default));
        }

        [Test]
        public void Datagram_SendDatagramAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var buffer = new byte[1];
            Assert.CatchAsync<OperationCanceledException>(
                async () => await OutgoingConnection.SendDatagramAsync(buffer, canceled.Token));
        }

        [Test]
        public void Datagram_SendDatagramAsync_Dispose()
        {
            OutgoingConnection.Dispose();
            Assert.CatchAsync<TransportException>(
                async () => await OutgoingConnection.SendDatagramAsync(OneBSendBuffer, default));
        }

        [Test]
        public void Datagram_SendDatagramAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await OutgoingConnection.SendDatagramAsync(OneBSendBuffer, canceled.Token));
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
                    ValueTask<int> sendTask = OutgoingConnection.SendDatagramAsync(sendBuffer, default);
                    ArraySegment<byte> receiveBuffer = await IncomingConnection.ReceiveDatagramAsync(source.Token);
                    Assert.AreEqual(await sendTask, receiveBuffer.Count);
                    Assert.AreEqual(sendBuffer, receiveBuffer);
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
