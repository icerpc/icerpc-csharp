// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Timeout(5000)]
    [Parallelizable(scope: ParallelScope.Fixtures)]
    [TestFixture("tcp")]
    [TestFixture("coloc")]
    public class SimpleNetworkConnectionTests
    {
        private ISimpleNetworkConnection ClientConnection => _clientConnection!;
        private ISimpleNetworkConnection ServerConnection => _serverConnection!;

        private ISimpleNetworkConnection? _clientConnection;
        private ISimpleNetworkConnection? _serverConnection;
        private readonly string _transport;

        public SimpleNetworkConnectionTests(string transport) => _transport = transport;

        [TestCase]
        public void SimpleNetworkConnection_Canceled()
        {
            Memory<byte> buffer = new byte[1];
            var buffers = new ReadOnlyMemory<byte>[] { buffer };

            using var source = new CancellationTokenSource();
            source.Cancel();
            CancellationToken token = source.Token;

            Assert.CatchAsync<OperationCanceledException>(async () => await ClientConnection.WriteAsync(buffers, token));
            Assert.CatchAsync<OperationCanceledException>(async () => await ClientConnection.ReadAsync(buffer, token));
        }

        [TestCase]
        public async Task SimpleNetworkConnection_ReceiveCancellation()
        {
            Memory<byte> buffer = new byte[1];
            var buffers = new ReadOnlyMemory<byte>[] { buffer };

            using var source = new CancellationTokenSource();
            CancellationToken token = source.Token;

            Task<int> task = ClientConnection.ReadAsync(buffer, token).AsTask();
            await Task.Delay(500);

            Assert.That(task.IsCompleted, Is.False);
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await task);
        }

        [TestCase]
        public async Task SimpleNetworkConnection_SendCancellation()
        {
            Memory<byte> buffer = new byte[1024 * 1024];
            var buffers = new ReadOnlyMemory<byte>[] { buffer };

            using var source = new CancellationTokenSource();
            Task task;
            while ((task = ClientConnection.WriteAsync(buffers, source.Token).AsTask()).IsCompleted)
            {
                // Wait for send to block.
            }

            Assert.That(task.IsCompleted, Is.False);
            await Task.Delay(500);
            Assert.That(task.IsCompleted, Is.False);

            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await task);
        }

        [TestCase]
        [Log(LogAttributeLevel.Trace)]
        public async Task SimpleNetworkConnection_SendReceive()
        {
            ReadOnlyMemory<byte> sendBuffer = new byte[] { 0x05, 0x06 };
            var sendBuffers = new ReadOnlyMemory<byte>[] { sendBuffer };
            Memory<byte> receiveBuffer = new byte[10];

            ValueTask task = ClientConnection.WriteAsync(sendBuffers, default);
            Assert.That(await ServerConnection.ReadAsync(receiveBuffer, default), Is.EqualTo(sendBuffer.Length));
            await task;
            Assert.That(receiveBuffer.ToArray()[0..2], Is.EqualTo(sendBuffer.ToArray()));

            ValueTask task2 = ClientConnection.WriteAsync(new ReadOnlyMemory<byte>[] { sendBuffer, sendBuffer }, default);
            Assert.That(await ServerConnection.ReadAsync(receiveBuffer, default), Is.EqualTo(sendBuffer.Length * 2));
            await task2;
            Assert.That(receiveBuffer.ToArray()[0..2], Is.EqualTo(sendBuffer.ToArray()));
            Assert.That(receiveBuffer.ToArray()[2..4], Is.EqualTo(sendBuffer.ToArray()));
        }

        [TestCase]
        public async Task SimpleNetworkConnection_DisposeAsync()
        {
            await _clientConnection!.DisposeAsync();
            await _serverConnection!.DisposeAsync();

            Memory<byte> buffer = new byte[1];
            var buffers = new ReadOnlyMemory<byte>[] { buffer };

            Assert.CatchAsync<ObjectDisposedException>(async () => await ClientConnection.WriteAsync(buffers, default));
            Assert.CatchAsync<ObjectDisposedException>(async () => await ClientConnection.ReadAsync(buffer, default));

            Assert.CatchAsync<ObjectDisposedException>(async () => await ServerConnection.WriteAsync(buffers, default));
            Assert.CatchAsync<ObjectDisposedException>(async () => await ServerConnection.ReadAsync(buffer, default));
        }

        [SetUp]
        public async Task SetUp()
        {
            Endpoint endpoint = TestHelper.GetTestEndpoint(transport: _transport, protocol: Protocol.Ice1);
            await using IListener<ISimpleNetworkConnection> listener =
                TestHelper.CreateSimpleServerTransport(_transport).Listen(endpoint,
                                                                          LogAttributeLoggerFactory.Instance.Logger);

            IClientTransport<ISimpleNetworkConnection> clientTransport =
                TestHelper.CreateSimpleClientTransport(_transport);
            _clientConnection = clientTransport.CreateConnection(listener.Endpoint,
                                                                 LogAttributeLoggerFactory.Instance.Logger);

            Task<ISimpleNetworkConnection> listenTask = listener.AcceptAsync();
            Task<NetworkConnectionInformation> connectTask = _clientConnection.ConnectAsync(default);
            _serverConnection = await listenTask;

            _ = await _serverConnection.ConnectAsync(default);
            _ = await connectTask;
        }

        [TearDown]
        public async Task TearDown()
        {
            if (_clientConnection is INetworkConnection clientConnection)
            {
                await clientConnection.DisposeAsync();
            }
            if (_serverConnection is INetworkConnection serverConnection)
            {
                await serverConnection.DisposeAsync();
            }
        }
    }
}
