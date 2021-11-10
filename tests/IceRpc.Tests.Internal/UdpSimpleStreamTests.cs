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
    public class UdpSimpleStreamTests
    {
        private static readonly ReadOnlyMemory<ReadOnlyMemory<byte>> _oneBWriteBuffer =
            new ReadOnlyMemory<byte>[] { new byte[1] };

        private ISimpleStream ClientStream => _clientStream!;
        private ISimpleStream ServerStream => _serverStream!;

        private ISimpleNetworkConnection? _clientConnection;
        private ISimpleStream? _clientStream;

        private readonly IClientTransport<ISimpleNetworkConnection> _clientTransport = new UdpClientTransport();

        private readonly IListener<ISimpleNetworkConnection> _listener;
        private ISimpleNetworkConnection? _serverConnection;
        private ISimpleStream? _serverStream;

        private readonly IServerTransport<ISimpleNetworkConnection> _serverTransport = new UdpServerTransport();

        private readonly bool _isIPv6;

        public UdpSimpleStreamTests(AddressFamily addressFamily)
        {
            _isIPv6 = addressFamily == AddressFamily.InterNetworkV6;
            string host = _isIPv6 ? "\"::1\"" : "127.0.0.1";
            _listener = _serverTransport.Listen($"udp -h {host} -p 0", LogAttributeLoggerFactory.Instance.Server);
        }

        [OneTimeSetUp]
        public async Task OneTimeSetupAsync()
        {
            Task<ISimpleNetworkConnection> acceptTask = _listener.AcceptAsync();

            _clientConnection =
                _clientTransport.CreateConnection(_listener.Endpoint, LogAttributeLoggerFactory.Instance.Client);
            Task<(ISimpleStream, NetworkConnectionInformation)> connectTask = _clientConnection.ConnectAsync(default);

            _serverConnection = await acceptTask;
            Task<(ISimpleStream, NetworkConnectionInformation)> serverConnectTask =
                _serverConnection.ConnectAsync(default);

            (_serverStream, _) = await serverConnectTask;
            (_clientStream, _) = await connectTask;
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
        public async Task UdpSimpleStream_MultipleReadWriteAsync(int clientConnectionCount, int size)
        {
            byte[] writeBuffer = new byte[size];
            new Random().NextBytes(writeBuffer);
            ReadOnlyMemory<ReadOnlyMemory<byte>> writeBuffers = new ReadOnlyMemory<byte>[] { writeBuffer };

            var clientConnectionList = new List<ISimpleNetworkConnection>();
            var clientStreamList = new List<ISimpleStream>();
            clientStreamList.Add(ClientStream);
            for (int i = 0; i < clientConnectionCount; ++i)
            {
                ISimpleNetworkConnection clientConnection =
                    _clientTransport.CreateConnection(_listener.Endpoint, LogAttributeLoggerFactory.Instance.Client);

                clientConnectionList.Add(clientConnection);
                (ISimpleStream clientStream, _) = await clientConnection.ConnectAsync(default);
                clientStreamList.Add(clientStream);
            }

            // Datagrams aren't reliable, try up to 5 times in case the datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    foreach (ISimpleStream stream in clientStreamList)
                    {
                        using var source = new CancellationTokenSource(1000);
                        ValueTask writeTask = stream.WriteAsync(writeBuffers, default);

                        Memory<byte> readBuffer = new byte[UdpUtils.MaxPacketSize];
                        int received = await ServerStream.ReadAsync(readBuffer, source.Token);

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
                    foreach (ISimpleStream stream in clientStreamList)
                    {
                        await stream.WriteAsync(writeBuffers, default);
                    }
                    foreach (ISimpleStream stream in clientStreamList)
                    {
                        using var source = new CancellationTokenSource(1000);
                        Memory<byte> readBuffer = new byte[UdpUtils.MaxPacketSize];
                        int received = await ServerStream.ReadAsync(readBuffer, source.Token);
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
        public void UdpSimpleStream_ReadAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            Memory<byte> readBuffer = new byte[UdpUtils.MaxPacketSize];
            ValueTask<int> readTask = ClientStream.ReadAsync(readBuffer, canceled.Token);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await readTask);
        }

        [Test]
        public async Task UdpSimpleStream_ReadAsync_DisposeAsync()
        {
            await _clientConnection!.DisposeAsync();
            Assert.CatchAsync<ObjectDisposedException>(async () => await ClientStream.ReadAsync(new byte[256], default));
        }

        [Test]
        public void UdpSimpleStream_WriteAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            byte[] buffer = new byte[1];
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientStream.WriteAsync(new ReadOnlyMemory<byte>[] { buffer }, canceled.Token));
        }

        [Test]
        public async Task UdpSimpleStream_WriteAsync_DisposeAsync()
        {
            await _clientConnection!.DisposeAsync();
            Assert.CatchAsync<ObjectDisposedException>(async () => await ClientStream.WriteAsync(_oneBWriteBuffer, default));
        }

        [Test]
        public void UdpSimpleStream_SendAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientStream.WriteAsync(_oneBWriteBuffer, canceled.Token));
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(4096)]
        public async Task UdpSimpleStream_ReadWriteAsync(int size)
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
                    ValueTask writeTask = ClientStream.WriteAsync(writeBuffers, default);
                    Memory<byte> readBuffer = new byte[UdpUtils.MaxPacketSize];
                    int received = await ServerStream.ReadAsync(readBuffer, source.Token);
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
