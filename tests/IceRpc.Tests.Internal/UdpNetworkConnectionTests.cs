// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Net.Sockets;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    [TestFixture(AddressFamily.InterNetwork)]
    [TestFixture(AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class UdpNetworkConnectionTests
    {
        private static readonly ReadOnlyMemory<ReadOnlyMemory<byte>> _oneBWriteBuffer =
            new ReadOnlyMemory<byte>[] { new byte[1] };

        private ISimpleNetworkConnection ClientConnection => _clientConnection!;
        private ISimpleNetworkConnection ServerConnection => _serverConnection!;

        private ISimpleNetworkConnection? _clientConnection;
        private readonly IClientTransport<ISimpleNetworkConnection> _clientTransport = new UdpClientTransport();
        private readonly IListener<ISimpleNetworkConnection> _listener;
        private ISimpleNetworkConnection? _serverConnection;
        private readonly IServerTransport<ISimpleNetworkConnection> _serverTransport = new UdpServerTransport();
        private readonly bool _isIPv6;

        public UdpNetworkConnectionTests(AddressFamily addressFamily)
        {
            _isIPv6 = addressFamily == AddressFamily.InterNetworkV6;
            string host = _isIPv6 ? "\"::1\"" : "127.0.0.1";
            _listener = _serverTransport.Listen($"udp -h {host} -p 0", LogAttributeLoggerFactory.Instance.Logger);
        }

        [OneTimeSetUp]
        public async Task OneTimeSetupAsync()
        {
            _serverConnection = await _listener.AcceptAsync();

            _clientConnection =
                _clientTransport.CreateConnection(_listener.Endpoint, LogAttributeLoggerFactory.Instance.Logger);
            _ = await _serverConnection.ConnectAsync(default);
            _ = await _clientConnection.ConnectAsync(default);
        }

        [OneTimeTearDown]
        public async Task ShutdownAsync()
        {
            if (_clientConnection is INetworkConnection clientConnection)
            {
                await clientConnection.DisposeAsync();
            }
            if (_serverConnection is INetworkConnection serverConnection)
            {
                await serverConnection.DisposeAsync();
            }
            await _listener.DisposeAsync();
        }

        [TestCase(1, 1)]
        [TestCase(1, 1024)]
        [TestCase(1, 4096)]
        [TestCase(2, 1024)]
        [TestCase(10, 1024)]
        public async Task UdpNetworkConnection_MultipleReadWriteAsync(int clientConnectionCount, int size)
        {
            byte[] writeBuffer = new byte[size];
            new Random().NextBytes(writeBuffer);
            ReadOnlyMemory<ReadOnlyMemory<byte>> writeBuffers = new ReadOnlyMemory<byte>[] { writeBuffer };

            var clientConnectionList = new List<ISimpleNetworkConnection>();
            for (int i = 0; i < clientConnectionCount; ++i)
            {
                ISimpleNetworkConnection clientConnection =
                    _clientTransport.CreateConnection(_listener.Endpoint, LogAttributeLoggerFactory.Instance.Logger);

                clientConnectionList.Add(clientConnection);
                _ = await clientConnection.ConnectAsync(default);
            }

            // Datagrams aren't reliable, try up to 5 times in case the datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    foreach (ISimpleNetworkConnection clientConnection in clientConnectionList)
                    {
                        using var source = new CancellationTokenSource(1000);
                        ValueTask writeTask = clientConnection.WriteAsync(writeBuffers, default);

                        Memory<byte> readBuffer = new byte[UdpUtils.MaxPacketSize];
                        int received = await ServerConnection.ReadAsync(readBuffer, source.Token);

                        Assert.AreEqual(writeBuffer.Length, received);
                        for (int i = 0; i < received; ++i)
                        {
                            Assert.AreEqual(writeBuffer[i], readBuffer.Span[i]);
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
                    foreach (ISimpleNetworkConnection clientConnection in clientConnectionList)
                    {
                        await clientConnection.WriteAsync(writeBuffers, default);
                    }
                    foreach (ISimpleNetworkConnection clientConnection in clientConnectionList)
                    {
                        using var source = new CancellationTokenSource(1000);
                        Memory<byte> readBuffer = new byte[UdpUtils.MaxPacketSize];
                        int received = await ServerConnection.ReadAsync(readBuffer, source.Token);
                        Assert.AreEqual(writeBuffer.Length, received);
                    }
                    break;
                }
                catch (OperationCanceledException)
                {
                }
            }
            Assert.AreNotEqual(0, count);

            await Task.WhenAll(clientConnectionList.Select(connection => connection.DisposeAsync().AsTask()));
        }

        [Test]
        public void UdpNetworkConnection_ReadAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            Memory<byte> readBuffer = new byte[UdpUtils.MaxPacketSize];
            ValueTask<int> readTask = ClientConnection.ReadAsync(readBuffer, canceled.Token);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await readTask);
        }

        [Test]
        public async Task UdpNetworkConnection_ReadAsync_DisposeAsync()
        {
            await _clientConnection!.DisposeAsync();
            Assert.CatchAsync<ObjectDisposedException>(async () => await ClientConnection.ReadAsync(new byte[256], default));
        }

        [Test]
        public void UdpNetworkConnection_WriteAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            byte[] buffer = new byte[1];
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientConnection.WriteAsync(new ReadOnlyMemory<byte>[] { buffer }, canceled.Token));
        }

        [Test]
        public async Task UdpNetworkConnection_WriteAsync_DisposeAsync()
        {
            await _clientConnection!.DisposeAsync();
            Assert.CatchAsync<ObjectDisposedException>(async () => await ClientConnection.WriteAsync(_oneBWriteBuffer, default));
        }

        [Test]
        public void UdpNetworkConnection_SendAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientConnection.WriteAsync(_oneBWriteBuffer, canceled.Token));
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(4096)]
        [Log(LogAttributeLevel.Trace)]
        public async Task UdpNetworkConnection_ReadWriteAsync(int size)
        {
            byte[] writeBuffer = new byte[size];
            new Random().NextBytes(writeBuffer);
            ReadOnlyMemory<ReadOnlyMemory<byte>> writeBuffers = new ReadOnlyMemory<byte>[] { writeBuffer };

            // Datagrams aren't reliable, try up to 5 times in case the datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    using var source = new CancellationTokenSource(1000);
                    ValueTask writeTask = ClientConnection.WriteAsync(writeBuffers, default);
                    Memory<byte> readBuffer = new byte[UdpUtils.MaxPacketSize];
                    int received = await ServerConnection.ReadAsync(readBuffer, source.Token);
                    Assert.AreEqual(writeBuffer.Length, received);
                    for (int i = 0; i < received; ++i)
                    {
                        Assert.AreEqual(writeBuffer[i], readBuffer.Span[i]);
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
