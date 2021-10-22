// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Net.Sockets;

namespace IceRpc.Tests.Internal
{
    [TestFixture(AddressFamily.InterNetwork)]
    [TestFixture(AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class UdpSocketTests : NetworkSocketBaseTest
    {
        private NetworkSocket ClientSocket => _clientSocket!;
        private NetworkSocket ServerSocket => _serverSocket!;
        private NetworkSocket? _clientSocket;
        private NetworkSocket? _serverSocket;

        public UdpSocketTests(AddressFamily addressFamily)
            : base("udp", tls: false, addressFamily)
        {
        }

        [SetUp]
        public async Task SetupAsync()
        {
            _serverSocket = await CreateServerNetworkSocketAsync();
            _clientSocket = await ConnectAsync();
        }

        [TearDown]
        public void TearDown()
        {
            _clientSocket?.Dispose();
            _serverSocket?.Dispose();
        }

        [TestCase(1, 1)]
        [TestCase(1, 1024)]
        [TestCase(1, 4096)]
        [TestCase(2, 1024)]
        [TestCase(10, 1024)]
        public async Task UdpSocket_MultipleSendReceiveAsync(int clientConnectionCount, int size)
        {
            byte[] sendBuffer = new byte[size];
            new Random().NextBytes(sendBuffer);
            ReadOnlyMemory<ReadOnlyMemory<byte>> sendBuffers = new ReadOnlyMemory<byte>[] { sendBuffer };

            List<NetworkSocket> clientSockets = new();
            clientSockets.Add(ClientSocket);
            for (int i = 0; i < clientConnectionCount; ++i)
            {
                clientSockets.Add(await ConnectAsync());
            }

            // Datagrams aren't reliable, try up to 5 times in case the datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    foreach (NetworkSocket connection in clientSockets)
                    {
                        using var source = new CancellationTokenSource(1000);
                        ValueTask sendTask = connection.SendAsync(sendBuffers, default);

                        Memory<byte> receiveBuffer = new byte[ServerSocket.DatagramMaxReceiveSize];
                        int received = await ServerSocket.ReceiveAsync(receiveBuffer, source.Token);

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
                    foreach (NetworkSocket connection in clientSockets)
                    {
                        await connection.SendAsync(sendBuffers, default);
                    }
                    foreach (NetworkSocket connection in clientSockets)
                    {
                        using var source = new CancellationTokenSource(1000);
                        Memory<byte> receiveBuffer = new byte[ServerSocket.DatagramMaxReceiveSize];
                        int received = await ServerSocket.ReceiveAsync(receiveBuffer, source.Token);
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
        public void UdpSocket_ReceiveAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            Memory<byte> receiveBuffer = new byte[ClientSocket.DatagramMaxReceiveSize];
            ValueTask<int> receiveTask = ClientSocket.ReceiveAsync(receiveBuffer, canceled.Token);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        }

        [Test]
        public void UdpSocket_ReceiveAsync_Dispose()
        {
            _clientSocket!.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.ReceiveAsync(new byte[256], default));
        }

        [Test]
        public void UdpSocket_SendAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            byte[] buffer = new byte[1];
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientSocket.SendAsync(new ReadOnlyMemory<byte>[] { buffer }, canceled.Token));
        }

        [Test]
        public void UdpSocket_SendAsync_Dispose()
        {
            _clientSocket!.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.SendAsync(OneBSendBuffer, default));
        }

        [Test]
        public void UdpSocket_SendAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientSocket.SendAsync(OneBSendBuffer, canceled.Token));
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(4096)]
        public async Task UdpSocket_SendReceiveAsync(int size)
        {
            byte[] sendBuffer = new byte[size];
            new Random().NextBytes(sendBuffer);
            ReadOnlyMemory<ReadOnlyMemory<byte>> sendBuffers = new ReadOnlyMemory<byte>[] { sendBuffer };

            // Datagrams aren't reliable, try up to 5 times in case the datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    using var source = new CancellationTokenSource(1000);
                    ValueTask sendTask = ClientSocket.SendAsync(sendBuffers, default);
                    Memory<byte> receiveBuffer = new byte[ServerSocket.DatagramMaxReceiveSize];
                    int received = await ServerSocket.ReceiveAsync(receiveBuffer, source.Token);
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
