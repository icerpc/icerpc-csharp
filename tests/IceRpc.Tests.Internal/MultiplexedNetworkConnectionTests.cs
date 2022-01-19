// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    // Test the multi-stream interface.
    [Timeout(5000)]
    [Parallelizable(ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class MultiplexedNetworkConnectionTests
    {
        private readonly IMultiplexedNetworkConnection _clientConnection;
        private readonly IMultiplexedNetworkConnection _serverConnection;
        private readonly ServiceProvider _serviceProvider;

        public MultiplexedNetworkConnectionTests()
        {
            _serviceProvider = new InternalTestServiceCollection()
                .AddTransient(_ => new SlicOptions()
                {
                    BidirectionalStreamMaxCount = 15,
                    UnidirectionalStreamMaxCount = 10
                })
                .BuildServiceProvider();

            Task<IMultiplexedNetworkConnection> serverTask = _serviceProvider.GetMultiplexedServerConnectionAsync();
            _clientConnection = _serviceProvider.GetMultiplexedClientConnectionAsync().Result;
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
        public async Task MultiplexedNetworkConnection_DisposeAsync()
        {
            ValueTask<IMultiplexedStream> acceptStreamTask = _serverConnection.AcceptStreamAsync(default);
            await _clientConnection.DisposeAsync();
            Assert.ThrowsAsync<ObjectDisposedException>(async () => await acceptStreamTask);
        }

        [Test]
        public async Task MultiplexedNetworkConnection_Dispose_StreamAbortedAsync()
        {
            IMultiplexedStream clientStream = _clientConnection.CreateStream(true);
            await clientStream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default);

            await _clientConnection.DisposeAsync();

            MultiplexedStreamAbortedException? ex;
            // Stream is aborted
            ex = Assert.ThrowsAsync<MultiplexedStreamAbortedException>(
                async () => await clientStream.ReadAsync(new byte[10], default));
            Assert.That(ex!.ErrorCode, Is.EqualTo((byte)MultiplexedStreamError.ConnectionAborted));

            // Can't create new stream
            clientStream = _clientConnection.CreateStream(true);
            Assert.ThrowsAsync<ConnectionClosedException>(
                async () => await clientStream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default));
        }

        [Test]
        public async Task MultiplexedNetworkConnection_AcceptStreamAsync()
        {
            IMultiplexedStream clientStream = _clientConnection.CreateStream(bidirectional: true);
            ValueTask<IMultiplexedStream> acceptTask = _serverConnection.AcceptStreamAsync(default);

            // The server-side won't accept the stream until the first frame is sent.
            await clientStream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default);

            IMultiplexedStream serverStream = await acceptTask;

            Assert.That(serverStream.IsBidirectional, Is.True);
            Assert.AreEqual(serverStream.Id, clientStream.Id);
        }

        [Test]
        public void MultiplexedNetworkConnection_AcceptStream_Cancellation()
        {
            using var source = new CancellationTokenSource();
            ValueTask<IMultiplexedStream> acceptTask = _serverConnection.AcceptStreamAsync(source.Token);
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await acceptTask);
        }

        [Test]
        public async Task MultiplexedNetworkConnection_AcceptStream_DisposeAsync()
        {
            await _clientConnection.DisposeAsync();
            Assert.CatchAsync<ObjectDisposedException>(async () => await _serverConnection.AcceptStreamAsync(default));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiplexedNetworkConnection_CreateStream(bool bidirectional)
        {
            IMultiplexedStream clientStream = _clientConnection.CreateStream(bidirectional);
            Assert.Throws<InvalidOperationException>(() => _ = clientStream.Id); // stream is not started
            Assert.AreEqual(bidirectional, clientStream.IsBidirectional);

            await clientStream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default);
            Assert.That(clientStream.Id, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task MultiplexedNetworkConnection_StreamMaxCount_BidirectionalAsync()
        {
            var clientStreams = new List<IMultiplexedStream>();
            var serverStreams = new List<IMultiplexedStream>();
            SlicOptions slicOptions = _serviceProvider.GetRequiredService<SlicOptions>();
            for (int i = 0; i < slicOptions!.BidirectionalStreamMaxCount; ++i)
            {
                IMultiplexedStream stream = _clientConnection.CreateStream(true);
                clientStreams.Add(stream);

                await stream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default);

                serverStreams.Add(await _serverConnection.AcceptStreamAsync(default));
                await serverStreams.Last().ReadAsync(new byte[10], default);
            }

            IMultiplexedStream clientStream = _clientConnection.CreateStream(true);
            ValueTask sendTask = clientStream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default);
            ValueTask<IMultiplexedStream> acceptTask = _serverConnection.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened. The stream isn't opened
            // on the client side until we have confirmation from the server that we can open a new stream, so
            // the send should not complete.
            Assert.That(sendTask.IsCompleted, Is.False);
            Assert.That(acceptTask.IsCompleted, Is.False);

            // Close one stream by sending EOS after receiving the payload.
            await serverStreams.Last().WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default);
            await clientStreams.Last().ReadAsync(new byte[10], default);

            // Now it should be possible to accept the new stream on the server side.
            await sendTask;
            _ = await acceptTask;
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiplexedNetworkConnection_StreamMaxCount_StressTestAsync(bool bidirectional)
        {
            SlicOptions slicOptions = _serviceProvider.GetRequiredService<SlicOptions>();
            int maxCount = bidirectional ?
                slicOptions!.BidirectionalStreamMaxCount :
                slicOptions!.UnidirectionalStreamMaxCount;
            int streamCount = 0;

            // Send many payloads and receive the payloads.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = SendAndReceiveAsync(_clientConnection.CreateStream(bidirectional));
            }

            // Receive all the payloads and send the payloads.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = ReceiveAndSendAsync(await _serverConnection.AcceptStreamAsync(default));
            }

            async Task SendAndReceiveAsync(IMultiplexedStream stream)
            {
                await stream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default);

                Assert.That(Thread.VolatileRead(ref streamCount), Is.LessThanOrEqualTo(maxCount));

                if (bidirectional)
                {
                    await stream.ReadAsync(new byte[10], default);
                }
            }

            async Task ReceiveAndSendAsync(IMultiplexedStream stream)
            {
                // Make sure the connection didn't accept more streams than it is allowed to.
                Assert.That(Thread.VolatileRead(ref streamCount), Is.LessThanOrEqualTo(maxCount));

                if (!bidirectional)
                {
                    // The stream is terminated as soon as the last frame of the request is received, so we have
                    // to decrement the count here before the request receive completes.
                    Interlocked.Decrement(ref streamCount);
                }

                _ = await stream.ReadAsync(new byte[10], default);

                if (bidirectional)
                {
                    Interlocked.Decrement(ref streamCount);
                }

                if (bidirectional)
                {
                    await stream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default);
                }
            }
        }

        [Test]
        public async Task MultiplexedNetworkConnection_StreamMaxCount_UnidirectionalAsync()
        {
            SlicOptions slicOptions = _serviceProvider.GetRequiredService<SlicOptions>();
            var clientStreams = new List<IMultiplexedStream>();
            for (int i = 0; i < slicOptions!.UnidirectionalStreamMaxCount; ++i)
            {
                IMultiplexedStream stream = _clientConnection.CreateStream(false);
                clientStreams.Add(stream);
                await stream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default);
            }

            IMultiplexedStream clientStream = _clientConnection.CreateStream(false);
            ValueTask sendTask = clientStream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default);

            // Accept a new unidirectional stream. This shouldn't allow the new stream send to complete since
            // the payload wasn't read yet on the stream.
            IMultiplexedStream serverStream = await _serverConnection.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened.The stream isn't opened
            // on the client side until we have confirmation from the server that we can open a new stream, so
            // the send should not complete.
            Assert.That(sendTask.IsCompleted, Is.False);

            // Close the server-side stream by receiving the payload from the stream.
            await serverStream.ReadAsync(new byte[10], default);

            // The send task of the new stream should now succeed.
            await sendTask;
        }

        [Test]
        public async Task MultiplexedNetworkConnection_SendAsync_Failure()
        {
            IMultiplexedStream stream = _clientConnection.CreateStream(false);
            await _clientConnection.DisposeAsync();
            Assert.CatchAsync<ConnectionClosedException>(
                async () => await stream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default));
        }

        [Test]
        public async Task MultiplexedNetworkConnection_SendAsync_FailureAsync()
        {
            IMultiplexedStream stream = _clientConnection.CreateStream(true);
            await stream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default);

            IMultiplexedStream serverStream = await _serverConnection.AcceptStreamAsync(default);
            await serverStream.ReadAsync(new byte[10], default);
            await _serverConnection.DisposeAsync();
            Assert.CatchAsync<MultiplexedStreamAbortedException>(
                async () => await serverStream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[10] }, true, default));
        }
    }
}
