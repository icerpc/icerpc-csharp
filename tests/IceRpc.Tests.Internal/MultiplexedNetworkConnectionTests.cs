// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.IO.Pipelines;

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
        public async Task MultiplexedNetworkConnection_Dispose_StreamDisposedAsync()
        {
            IMultiplexedStream clientStream = _clientConnection.CreateStream(true);
            await clientStream.Output.WriteAsync(new byte[10], true, default);

            await _clientConnection.DisposeAsync();

            Assert.ThrowsAsync<InvalidOperationException>(async () => await clientStream.Input.ReadAsync());

            // Can't create new stream
            clientStream = _clientConnection.CreateStream(true);
            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await clientStream.Output.WriteAsync(new byte[10], true, default));
        }

        [Test]
        public async Task MultiplexedNetworkConnection_AcceptStreamAsync()
        {
            IMultiplexedStream clientStream = _clientConnection.CreateStream(bidirectional: true);
            ValueTask<IMultiplexedStream> acceptTask = _serverConnection.AcceptStreamAsync(default);

            // The server-side won't accept the stream until the first frame is sent.
            await clientStream.Output.WriteAsync(new byte[10], true, default);

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

            await clientStream.Output.WriteAsync(new byte[10], true, default);
            Assert.That(clientStream.Id, Is.GreaterThanOrEqualTo(0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiplexedNetworkConnection_StreamMaxCount_BidirectionalAsync(bool complete)
        {
            var clientStreams = new List<IMultiplexedStream>();
            var serverStreams = new List<IMultiplexedStream>();
            IMultiplexedStream clientStream;
            IMultiplexedStream serverStream;
            SlicOptions slicOptions = _serviceProvider.GetRequiredService<SlicOptions>();
            ReadResult readResult;
            for (int i = 0; i < slicOptions!.BidirectionalStreamMaxCount; ++i)
            {
                clientStream = _clientConnection.CreateStream(true);
                clientStreams.Add(clientStream);

                await clientStream.Output.WriteAsync(new byte[10], completeWhenDone: true, default);

                serverStream = await _serverConnection.AcceptStreamAsync(default);
                serverStreams.Add(serverStream);
                readResult = await serverStream.Input.ReadAsync();
                serverStream.Input.AdvanceTo(readResult.Buffer.End);
                Assert.That(readResult.IsCompleted);
            }

            clientStream = _clientConnection.CreateStream(true);
            ValueTask<FlushResult> sendTask = clientStream.Output.WriteAsync(new byte[10], true, default);
            ValueTask<IMultiplexedStream> acceptTask = _serverConnection.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened. The stream isn't opened
            // on the client side until we have confirmation from the server that we can open a new stream, so
            // the send should not complete.
            Assert.That(sendTask.IsCompleted, Is.False);
            Assert.That(acceptTask.IsCompleted, Is.False);

            // Close one stream by sending EOS after receiving the payload.
            await serverStreams.Last().Output.WriteAsync(new byte[10], completeWhenDone: true, default);

            if (complete)
            {
                await clientStreams.Last().Input.CompleteAsync();
            }
            else
            {
                readResult = await clientStreams.Last().Input.ReadAsync(default);
                Assert.That(readResult.Buffer.Length, Is.EqualTo(10));
                clientStreams.Last().Input.AdvanceTo(readResult.Buffer.End);
                if (!readResult.IsCompleted)
                {
                    readResult = await clientStreams.Last().Input.ReadAsync(default);
                }
                Assert.That(readResult.IsCompleted);
            }

            // Ensure streams are shutdown.
            await serverStreams.Last().WaitForShutdownAsync(default);
            await clientStreams.Last().WaitForShutdownAsync(default);

            // Now it should be possible to accept the new stream on the server side.
            _ = await acceptTask;
            await sendTask;
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
                await stream.Output.WriteAsync(new byte[10], completeWhenDone: true, default);

                Assert.That(Thread.VolatileRead(ref streamCount), Is.LessThanOrEqualTo(maxCount));

                if (bidirectional)
                {
                    // Consume the data (the stream isn't shutdown until the data is consumed)
                    ReadResult readResult = await stream.Input.ReadAsync(default);
                    Assert.That(readResult.IsCompleted, Is.True);
                    stream.Input.AdvanceTo(readResult.Buffer.End);
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

                ReadResult readResult = await stream.Input.ReadAsync();
                Assert.That(readResult.IsCompleted, Is.True);
                stream.Input.AdvanceTo(readResult.Buffer.End);

                if (bidirectional)
                {
                    Interlocked.Decrement(ref streamCount);
                }

                if (bidirectional)
                {
                    await stream.Output.WriteAsync(new byte[10], completeWhenDone: true, default);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiplexedNetworkConnection_StreamMaxCount_UnidirectionalAsync(bool complete)
        {
            SlicOptions slicOptions = _serviceProvider.GetRequiredService<SlicOptions>();
            var clientStreams = new List<IMultiplexedStream>();
            for (int i = 0; i < slicOptions!.UnidirectionalStreamMaxCount; ++i)
            {
                IMultiplexedStream stream = _clientConnection.CreateStream(false);
                clientStreams.Add(stream);
                await stream.Output.WriteAsync(new byte[10], completeWhenDone: true, default);
            }

            IMultiplexedStream clientStream = _clientConnection.CreateStream(false);
            ValueTask<FlushResult> sendTask = clientStream.Output.WriteAsync(
                new byte[10],
                completeWhenDone: true,
                default);

            // Accept a new unidirectional stream. This shouldn't allow the new stream send to complete since
            // the payload wasn't read yet on the stream.
            IMultiplexedStream serverStream = await _serverConnection.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened. The stream isn't opened
            // on the client side until we have confirmation from the server that we can open a new stream, so
            // the send should not complete.
            Assert.That(sendTask.IsCompleted, Is.False);

            // Close the server-side stream by consuming the payload from the stream or by completing the input
            // pipe of the stream.
            if (complete)
            {
                await serverStream.Input.CompleteAsync();
            }
            else
            {
                ReadResult readResult = await serverStream.Input.ReadAsync(default);
                Assert.That(readResult.IsCompleted, Is.True);
                Assert.That(sendTask.IsCompleted, Is.False);
                serverStream.Input.AdvanceTo(readResult.Buffer.End);
            }

            // The send task of the new stream should now succeed.
            await sendTask;
        }

        [Test]
        public async Task MultiplexedNetworkConnection_SendAsync_Failure()
        {
            IMultiplexedStream stream = _clientConnection.CreateStream(false);
            await _clientConnection.DisposeAsync();
            Assert.CatchAsync<ObjectDisposedException>(
                async () => await stream.Output.WriteAsync(new byte[10], true, default));
        }

        [Test]
        public async Task MultiplexedNetworkConnection_SendAsync_FailureAsync()
        {
            IMultiplexedStream clientStream = _clientConnection.CreateStream(true);
            await clientStream.Output.WriteAsync(new byte[10], true, default);

            IMultiplexedStream serverStream = await _serverConnection.AcceptStreamAsync(default);
            ReadResult readResult = await serverStream.Input.ReadAsync();
            Assert.That(readResult.IsCompleted, Is.True);
            await _serverConnection.DisposeAsync();

            Assert.CatchAsync<InvalidOperationException>(
                async () => await serverStream.Output.WriteAsync(new byte[10], true, default));
        }
    }
}
