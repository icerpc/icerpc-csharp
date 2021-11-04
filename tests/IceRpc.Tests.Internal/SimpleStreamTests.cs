// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Timeout(5000)]
    [Parallelizable(scope: ParallelScope.Fixtures)]
    [TestFixture("tcp")]
    [TestFixture("coloc")]
    public class SimpleStreamTests
    {
        private ISimpleStream ClientStream => _clientSimpleStreamConnection!;
        private ISimpleStream ServerStream => _serverSimpleStreamConnection!;

        private ISimpleNetworkConnection? _clientConnection;
        private ISimpleStream? _clientSimpleStreamConnection;
        private ISimpleNetworkConnection? _serverConnection;
        private ISimpleStream? _serverSimpleStreamConnection;
        private readonly string _transport;

        public SimpleStreamTests(string transport) => _transport = transport;

        [TestCase]
        public void SimpleStream_Canceled()
        {
            Memory<byte> buffer = new byte[1];
            var buffers = new ReadOnlyMemory<byte>[] { buffer };

            using var source = new CancellationTokenSource();
            source.Cancel();
            CancellationToken token = source.Token;

            Assert.CatchAsync<OperationCanceledException>(async () => await ClientStream.WriteAsync(buffers, token));
            Assert.CatchAsync<OperationCanceledException>(async () => await ClientStream.ReadAsync(buffer, token));
        }

        [TestCase]
        public async Task SimpleStream_ReceiveCancellation()
        {
            Memory<byte> buffer = new byte[1];
            var buffers = new ReadOnlyMemory<byte>[] { buffer };

            using var source = new CancellationTokenSource();
            CancellationToken token = source.Token;

            Task<int> task = ClientStream.ReadAsync(buffer, token).AsTask();
            await Task.Delay(500);

            Assert.That(task.IsCompleted, Is.False);
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await task);
        }

        [TestCase]
        public async Task SimpleStream_SendCancellation()
        {
            Memory<byte> buffer = new byte[1024 * 1024];
            var buffers = new ReadOnlyMemory<byte>[] { buffer };

            using var source = new CancellationTokenSource();
            while (ClientStream.WriteAsync(buffers, source.Token).AsTask().IsCompleted)
            {
                // Wait for send to block.
            }

            Task task = ClientStream.WriteAsync(buffers, source.Token).AsTask();
            await Task.Delay(500);

            Assert.That(task.IsCompleted, Is.False);
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await task);
        }

        [TestCase]
        [Log(LogAttributeLevel.Trace)]
        public async Task SimpleStream_SendReceive()
        {
            ReadOnlyMemory<byte> sendBuffer = new byte[] { 0x05, 0x06 };
            var sendBuffers = new ReadOnlyMemory<byte>[] { sendBuffer };
            Memory<byte> receiveBuffer = new byte[10];

            ValueTask task = ClientStream.WriteAsync(sendBuffers, default);
            Assert.That(await ServerStream.ReadAsync(receiveBuffer, default), Is.EqualTo(sendBuffer.Length));
            await task;
            Assert.That(receiveBuffer.ToArray()[0..2], Is.EqualTo(sendBuffer.ToArray()));

            ValueTask task2 = ClientStream.WriteAsync(new ReadOnlyMemory<byte>[] { sendBuffer, sendBuffer }, default);
            Assert.That(await ServerStream.ReadAsync(receiveBuffer, default), Is.EqualTo(sendBuffer.Length * 2));
            await task2;
            Assert.That(receiveBuffer.ToArray()[0..2], Is.EqualTo(sendBuffer.ToArray()));
            Assert.That(receiveBuffer.ToArray()[2..4], Is.EqualTo(sendBuffer.ToArray()));
        }

        [TestCase]
        public void SimpleStream_Dispose()
        {
            _clientConnection!.Dispose();
            _serverConnection!.Dispose();

            Memory<byte> buffer = new byte[1];
            var buffers = new ReadOnlyMemory<byte>[] { buffer };

            Assert.CatchAsync<ObjectDisposedException>(async () => await ClientStream.WriteAsync(buffers, default));
            Assert.CatchAsync<ObjectDisposedException>(async () => await ClientStream.ReadAsync(buffer, default));

            Assert.CatchAsync<ObjectDisposedException>(async () => await ServerStream.WriteAsync(buffers, default));
            Assert.CatchAsync<ObjectDisposedException>(async () => await ServerStream.ReadAsync(buffer, default));
        }

        [SetUp]
        public async Task SetUp()
        {
            Endpoint endpoint = TestHelper.GetTestEndpoint(transport: _transport, protocol: Protocol.Ice1);
            using IListener<ISimpleNetworkConnection> listener =
                TestHelper.CreateSimpleServerTransport(_transport).Listen(endpoint, LogAttributeLoggerFactory.Instance);

            IClientTransport<ISimpleNetworkConnection> clientTransport =
                TestHelper.CreateSimpleClientTransport(_transport);
            _clientConnection = clientTransport.CreateConnection(listener.Endpoint, LogAttributeLoggerFactory.Instance);

            Task<ISimpleNetworkConnection> listenTask = listener.AcceptAsync();
            Task<(ISimpleStream, NetworkConnectionInformation)> connectTask = _clientConnection.ConnectAsync(default);
            _serverConnection = await listenTask;

            (_serverSimpleStreamConnection, _) = await _serverConnection.ConnectAsync(default);
            (_clientSimpleStreamConnection, _) = await connectTask;
        }

        [TearDown]
        public void TearDown()
        {
            _clientConnection?.Dispose();
            _serverConnection?.Dispose();
        }
    }
}
