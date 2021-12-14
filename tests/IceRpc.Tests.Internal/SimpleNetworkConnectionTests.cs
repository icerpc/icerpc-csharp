// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Timeout(5000)]
    [Parallelizable(scope: ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [TestFixture("tcp")]
    [TestFixture("coloc")]
    public class SimpleNetworkConnectionTests
    {
        private readonly ISimpleNetworkConnection _clientConnection;
        private readonly ISimpleNetworkConnection _serverConnection;
        private readonly ServiceProvider _serviceProvider;

        public SimpleNetworkConnectionTests(string transport)
        {
            _serviceProvider = new InternalTestServiceCollection().UseTransport(transport).BuildServiceProvider();

            Task<ISimpleNetworkConnection> serverTask = _serviceProvider.GetSimpleServerConnectionAsync();
            _clientConnection = _serviceProvider.GetSimpleClientConnectionAsync().Result;
            _serverConnection = serverTask.Result;
        }

        [TearDown]
        public async ValueTask DisposeAsync()
        {
            await _clientConnection.DisposeAsync();
            await _serverConnection.DisposeAsync();
            await _serviceProvider.DisposeAsync();
        }

        [Test]
        public void SimpleNetworkConnection_Canceled()
        {
            Memory<byte> buffer = new byte[1];
            var buffers = new ReadOnlyMemory<byte>[] { buffer };

            using var source = new CancellationTokenSource();
            source.Cancel();
            CancellationToken token = source.Token;

            Assert.CatchAsync<OperationCanceledException>(async () => await _clientConnection.WriteAsync(buffers, token));
            Assert.CatchAsync<OperationCanceledException>(async () => await _clientConnection.ReadAsync(buffer, token));
        }

        [Test]
        public async Task SimpleNetworkConnection_ReceiveCancellation()
        {
            Memory<byte> buffer = new byte[1];
            var buffers = new ReadOnlyMemory<byte>[] { buffer };

            using var source = new CancellationTokenSource();
            CancellationToken token = source.Token;

            Task<int> task = _clientConnection.ReadAsync(buffer, token).AsTask();
            await Task.Delay(500);

            Assert.That(task.IsCompleted, Is.False);
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await task);
        }

        // TODO: flow control doesn't appear to work as expected and this test sporadically fails. This needs to be
        // investigated.
        // [Test]
        // public async Task SimpleNetworkConnection_SendCancellation()
        // {
        //     Memory<byte> buffer = new byte[1024 * 1024];
        //     var buffers = new ReadOnlyMemory<byte>[] { buffer };

        //     using var source = new CancellationTokenSource();
        //     while (!_clientConnection.WriteAsync(buffers, source.Token).AsTask().IsCompleted)
        //     {
        //         // Wait for send to block.
        //     }

        //     Task task = _clientConnection.WriteAsync(buffers, source.Token).AsTask();
        //     Assert.That(task.IsCompleted, Is.False);
        //     await Task.Delay(500);
        //     Assert.That(task.IsCompleted, Is.False);

        //     source.Cancel();
        //     Assert.CatchAsync<OperationCanceledException>(async () => await task);
        // }

        [Test]
        public async Task SimpleNetworkConnection_SendReceive()
        {
            ReadOnlyMemory<byte> sendBuffer = new byte[] { 0x05, 0x06 };
            var sendBuffers = new ReadOnlyMemory<byte>[] { sendBuffer };
            Memory<byte> receiveBuffer = new byte[10];

            ValueTask task = _clientConnection.WriteAsync(sendBuffers, default);
            Assert.That(await _serverConnection.ReadAsync(receiveBuffer, default), Is.EqualTo(sendBuffer.Length));
            await task;
            Assert.That(receiveBuffer.ToArray()[0..2], Is.EqualTo(sendBuffer.ToArray()));

            ValueTask task2 = _clientConnection.WriteAsync(new ReadOnlyMemory<byte>[] { sendBuffer, sendBuffer }, default);
            Assert.That(await _serverConnection.ReadAsync(receiveBuffer, default), Is.EqualTo(sendBuffer.Length * 2));
            await task2;
            Assert.That(receiveBuffer.ToArray()[0..2], Is.EqualTo(sendBuffer.ToArray()));
            Assert.That(receiveBuffer.ToArray()[2..4], Is.EqualTo(sendBuffer.ToArray()));
        }

        [Test]
        public async Task SimpleNetworkConnection_DisposeAsync()
        {
            await _clientConnection!.DisposeAsync();
            await _serverConnection!.DisposeAsync();

            Memory<byte> buffer = new byte[1];
            var buffers = new ReadOnlyMemory<byte>[] { buffer };

            Assert.CatchAsync<ObjectDisposedException>(async () => await _clientConnection.WriteAsync(buffers, default));
            Assert.CatchAsync<ObjectDisposedException>(async () => await _clientConnection.ReadAsync(buffer, default));

            Assert.CatchAsync<ObjectDisposedException>(async () => await _serverConnection.WriteAsync(buffers, default));
            Assert.CatchAsync<ObjectDisposedException>(async () => await _serverConnection.ReadAsync(buffer, default));
        }
    }
}
