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

            Assert.CatchAsync<OperationCanceledException>(async () => await ClientStream.SendAsync(buffers, token));
            Assert.CatchAsync<OperationCanceledException>(async () => await ClientStream.ReceiveAsync(buffer, token));
        }

        [TestCase]
        public async Task SingleStreamConnection_ReceiveCancellation()
        {
            Memory<byte> buffer = new byte[1];
            var buffers = new ReadOnlyMemory<byte>[] { buffer };

            using var source = new CancellationTokenSource();
            CancellationToken token = source.Token;

            Task<int> task = ClientStream.ReceiveAsync(buffer, token).AsTask();
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
            while (ClientStream.SendAsync(buffers, source.Token).AsTask().IsCompleted)
            {
                // Wait for send to block.
            }

            Task task = ClientStream.SendAsync(buffers, source.Token).AsTask();
            await Task.Delay(500);

            Assert.That(task.IsCompleted, Is.False);
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await task);
        }

        [TestCase]
        public async Task SingleStreamConnection_SendReceive()
        {
            ReadOnlyMemory<byte> sendBuffer = new byte[] { 0x05, 0x06 };
            var sendBuffers = new ReadOnlyMemory<byte>[] { sendBuffer };
            Memory<byte> receiveBuffer = new byte[10];

            ValueTask task = ClientStream.SendAsync(sendBuffers, default);
            Assert.That(await ServerStream.ReceiveAsync(receiveBuffer, default), Is.EqualTo(sendBuffer.Length));
            await task;
            Assert.That(receiveBuffer.ToArray()[0..2], Is.EqualTo(sendBuffer.ToArray()));

            ValueTask task2 = ClientStream.SendAsync(new ReadOnlyMemory<byte>[] { sendBuffer, sendBuffer }, default);
            Assert.That(await ServerStream.ReceiveAsync(receiveBuffer, default), Is.EqualTo(sendBuffer.Length * 2));
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

            Assert.CatchAsync<TransportException>(async () => await ClientStream.SendAsync(buffers, default));
            Assert.CatchAsync <TransportException>(async () => await ClientStream.ReceiveAsync(buffer, default));

            Assert.CatchAsync<TransportException>(async () => await ServerStream.SendAsync(buffers, default));
            Assert.CatchAsync<TransportException>(async () => await ServerStream.ReceiveAsync(buffer, default));
        }

        [SetUp]
        public async Task SetUp()
        {
            Endpoint endpoint = TestHelper.GetTestEndpoint(transport: _transport);
            using IListener listener = TestHelper.CreateServerTransport(endpoint).Listen(
                    endpoint,
                    LogAttributeLoggerFactory.Instance).Listener!;

            Task<INetworkConnection> acceptTask = AcceptAsync(listener);
            _clientConnection = await ConnectAsync(listener.Endpoint);
            _serverConnection = await acceptTask;

            ValueTask<ISingleStreamConnection> serverTask = _serverConnection.GetSingleStreamConnectionAsync(default);
            _clientSingleStreamConnection = await _clientConnection.GetSingleStreamConnectionAsync(default);
            _serverSingleStreamConnection = await serverTask;

            async Task<INetworkConnection> AcceptAsync(IListener listener)
            {
                INetworkConnection networkConnection = await listener.AcceptAsync();
                await networkConnection.ConnectAsync(default);
                return networkConnection;
            }

            async Task<INetworkConnection> ConnectAsync(Endpoint clientEndpoint)
            {
                IClientTransport clientTransport = TestHelper.CreateClientTransport(clientEndpoint);
                INetworkConnection networkConnection = clientTransport.CreateConnection(
                        clientEndpoint,
                        LogAttributeLoggerFactory.Instance);
                await networkConnection.ConnectAsync(default);
                return networkConnection;
            }
        }

        [TearDown]
        public void TearDown()

        {
            _clientConnection?.Close(new ConnectionClosedException());
            _serverConnection?.Close(new ConnectionClosedException());
        }
    }
}
