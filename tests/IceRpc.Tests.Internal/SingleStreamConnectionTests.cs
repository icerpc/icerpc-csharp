// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Timeout(5000)]
    [Parallelizable(scope: ParallelScope.Fixtures)]
    [TestFixture("tcp")]
    [TestFixture("coloc")]
    public class SingleStreamConnectionTests
    {
        private ISingleStreamConnection ClientStream => _clientSingleStreamConnection!;
        private ISingleStreamConnection ServerStream => _serverSingleStreamConnection!;

        private INetworkConnection? _clientConnection;
        private ISingleStreamConnection? _clientSingleStreamConnection;
        private INetworkConnection? _serverConnection;
        private ISingleStreamConnection? _serverSingleStreamConnection;
        private readonly string _transport;

        public SingleStreamConnectionTests(string transport) => _transport = transport;

        [TestCase]
        public void SingleStreamConnection_Canceled()
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
        public async Task SingleStreamConnection_ReceiveCancellation()
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
        public async Task SingleStreamConnection_SendCancellation()
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
        public async Task SingleStreamConnection_SendReceive()
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
        public void SingleStreamConnection_Close()
        {
            _clientConnection!.Close();
            _serverConnection!.Close();

            Memory<byte> buffer = new byte[1];
            var buffers = new ReadOnlyMemory<byte>[] { buffer };

            Assert.CatchAsync<TransportException>(async () => await ClientStream.WriteAsync(buffers, default));
            Assert.CatchAsync <TransportException>(async () => await ClientStream.ReadAsync(buffer, default));

            Assert.CatchAsync<TransportException>(async () => await ServerStream.WriteAsync(buffers, default));
            Assert.CatchAsync<TransportException>(async () => await ServerStream.ReadAsync(buffer, default));
        }

        [SetUp]
        public async Task SetUp()
        {
            Endpoint endpoint = TestHelper.GetTestEndpoint(transport: _transport);
            using IListener listener = TestHelper.CreateServerTransport(endpoint).Listen(endpoint).Listener!;

            IClientTransport clientTransport = TestHelper.CreateClientTransport(listener.Endpoint);
            _clientConnection = clientTransport.CreateConnection(listener.Endpoint);

            ValueTask<INetworkConnection> acceptTask = listener.AcceptAsync();
            ValueTask<ISingleStreamConnection> connectTask = _clientConnection.ConnectSingleStreamConnectionAsync(default);
            _serverConnection = await acceptTask;
            _clientSingleStreamConnection = await connectTask;
            _serverSingleStreamConnection = await _serverConnection.ConnectSingleStreamConnectionAsync(default);
        }

        [TearDown]
        public void TearDown()
        {
            _clientConnection?.Close(new ConnectionClosedException());
            _serverConnection?.Close(new ConnectionClosedException());
        }
    }
}
