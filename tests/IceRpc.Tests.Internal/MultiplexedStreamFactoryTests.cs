// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    // Test the multi-stream interface.
    [Timeout(5000)]
    public class MultiplexedStreamFactoryTests : MultiplexedStreamFactoryBaseTest
    {
        private static readonly SlicOptions _serverSlicOptions = new()
            {
                BidirectionalStreamMaxCount = 15,
                UnidirectionalStreamMaxCount = 10
            };

        public MultiplexedStreamFactoryTests()
            : base(serverOptions: _serverSlicOptions)
        {
        }

        [Test]
        public void MultiplexedStreamFactory_Dispose()
        {
            ValueTask<IMultiplexedStream> acceptStreamTask = ServerMultiplexedStreamFactory.AcceptStreamAsync(default);
            ClientConnection.Dispose();
            Assert.ThrowsAsync<ConnectionLostException>(async () => await acceptStreamTask);
        }

        [Test]
        public async Task MultiplexedStreamFactory_Dispose_StreamAbortedAsync()
        {
            IMultiplexedStream clientStream = ClientMultiplexedStreamFactory.CreateStream(true);
            await clientStream.WriteAsync(CreateSendPayload(clientStream), true, default);

            ClientConnection.Dispose();

            StreamAbortedException? ex;
            // Stream is aborted
            ex = Assert.ThrowsAsync<StreamAbortedException>(
                async () => await clientStream.ReadAsync(CreateReceivePayload(), default));
            Assert.That(ex!.ErrorCode, Is.EqualTo(StreamError.ConnectionAborted));

            // Can't create new stream
            clientStream = ClientMultiplexedStreamFactory.CreateStream(true);
            Assert.ThrowsAsync<ConnectionClosedException>(
                async () => await clientStream.WriteAsync(CreateSendPayload(clientStream), true, default));
        }

        [Test]
        public async Task MultiplexedStreamFactory_AcceptStreamAsync()
        {
            IMultiplexedStream clientStream = ClientMultiplexedStreamFactory.CreateStream(bidirectional: true);
            ValueTask<IMultiplexedStream> acceptTask = ServerMultiplexedStreamFactory.AcceptStreamAsync(default);

            // The server-side won't accept the stream until the first frame is sent.
            await clientStream.WriteAsync(CreateSendPayload(clientStream), true, default);

            IMultiplexedStream serverStream = await acceptTask;

            Assert.That(serverStream.IsBidirectional, Is.True);
            Assert.AreEqual(serverStream.Id, clientStream.Id);
        }

        [Test]
        public void MultiplexedStreamFactory_AcceptStream_Cancellation()
        {
            using var source = new CancellationTokenSource();
            ValueTask<IMultiplexedStream> acceptTask = ServerMultiplexedStreamFactory.AcceptStreamAsync(source.Token);
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await acceptTask);
        }

        [Test]
        public void MultiplexedStreamFactory_AcceptStream_Failure()
        {
            ClientConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ServerMultiplexedStreamFactory.AcceptStreamAsync(default));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiplexedStreamFactory_CreateStream(bool bidirectional)
        {
            IMultiplexedStream clientStream = ClientMultiplexedStreamFactory.CreateStream(bidirectional);
            Assert.Throws<InvalidOperationException>(() => _ = clientStream.Id); // stream is not started
            Assert.AreEqual(bidirectional, clientStream.IsBidirectional);

            await clientStream.WriteAsync(CreateSendPayload(clientStream), true, default);
            Assert.That(clientStream.Id, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task MultiplexedStreamFactory_StreamMaxCount_BidirectionalAsync()
        {
            var clientStreams = new List<IMultiplexedStream>();
            var serverStreams = new List<IMultiplexedStream>();
            for (int i = 0; i < _serverSlicOptions!.BidirectionalStreamMaxCount; ++i)
            {
                IMultiplexedStream stream = ClientMultiplexedStreamFactory.CreateStream(true);
                clientStreams.Add(stream);

                await stream.WriteAsync(CreateSendPayload(stream), true, default);

                serverStreams.Add(await ServerMultiplexedStreamFactory.AcceptStreamAsync(default));
                await serverStreams.Last().ReadAsync(CreateReceivePayload(), default);
            }

            // Ensure the client side accepts streams to receive data.
            ValueTask<IMultiplexedStream> acceptClientStream = ClientMultiplexedStreamFactory.AcceptStreamAsync(default);

            IMultiplexedStream clientStream = ClientMultiplexedStreamFactory.CreateStream(true);
            ValueTask sendTask = clientStream.WriteAsync(CreateSendPayload(clientStream), true, default);
            ValueTask<IMultiplexedStream> acceptTask = ServerMultiplexedStreamFactory.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened. The stream isn't opened
            // on the client side until we have confirmation from the server that we can open a new stream, so
            // the send should not complete.
            Assert.That(sendTask.IsCompleted, Is.False);
            Assert.That(acceptTask.IsCompleted, Is.False);

            // Close one stream by sending EOS after receiving the payload.
            await serverStreams.Last().WriteAsync(CreateSendPayload(serverStreams.Last()), true, default);
            await clientStreams.Last().ReadAsync(CreateReceivePayload(), default);
            Assert.That(acceptClientStream.IsCompleted, Is.False);

            // Now it should be possible to accept the new stream on the server side.
            await sendTask;
            _ = await acceptTask;
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiplexedStreamFactory_StreamMaxCount_StressTestAsync(bool bidirectional)
        {
            int maxCount = bidirectional ?
                _serverSlicOptions!.BidirectionalStreamMaxCount :
                _serverSlicOptions!.UnidirectionalStreamMaxCount;
            int streamCount = 0;

            // Ensure the client side accepts streams to receive payloads.
            _ = ClientMultiplexedStreamFactory.AcceptStreamAsync(default).AsTask();

            // Send many payloads and receive the payloads.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = SendAndReceiveAsync(ClientMultiplexedStreamFactory.CreateStream(bidirectional));
            }

            // Receive all the payloads and send the payloads.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = ReceiveAndSendAsync(await ServerMultiplexedStreamFactory.AcceptStreamAsync(default));
            }

            async Task SendAndReceiveAsync(IMultiplexedStream stream)
            {
                await stream.WriteAsync(CreateSendPayload(stream), true, default);

                Assert.That(Thread.VolatileRead(ref streamCount), Is.LessThanOrEqualTo(maxCount));

                if (bidirectional)
                {
                    await stream.ReadAsync(CreateReceivePayload(), default);
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

                _ = await stream.ReadAsync(CreateReceivePayload(), default);

                if (bidirectional)
                {
                    Interlocked.Decrement(ref streamCount);
                }

                if (bidirectional)
                {
                    await stream.WriteAsync(CreateSendPayload(stream), true, default);
                }
            }
        }

        [Test]
        public async Task MultiplexedStreamFactory_StreamMaxCount_UnidirectionalAsync()
        {
            var clientStreams = new List<IMultiplexedStream>();
            for (int i = 0; i < _serverSlicOptions!.UnidirectionalStreamMaxCount; ++i)
            {
                IMultiplexedStream stream = ClientMultiplexedStreamFactory.CreateStream(false);
                clientStreams.Add(stream);
                await stream.WriteAsync(CreateSendPayload(stream), true, default);
            }

            // Ensure the client side accepts streams to receive acknowledgement of stream completion.
            ValueTask<IMultiplexedStream> acceptClientStream = ClientMultiplexedStreamFactory.AcceptStreamAsync(default);

            IMultiplexedStream clientStream = ClientMultiplexedStreamFactory.CreateStream(false);
            ValueTask sendTask = clientStream.WriteAsync(CreateSendPayload(clientStream), true, default);

            // Accept a new unidirectional stream. This shouldn't allow the new stream send to complete since
            // the payload wasn't read yet on the stream.
            IMultiplexedStream serverStream = await ServerMultiplexedStreamFactory.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened.The stream isn't opened
            // on the client side until we have confirmation from the server that we can open a new stream, so
            // the send should not complete.
            Assert.That(sendTask.IsCompleted, Is.False);

            // Close the server-side stream by receiving the payload from the stream.
            await serverStream.ReadAsync(CreateReceivePayload(), default);

            Assert.That(acceptClientStream.IsCompleted, Is.False);

            // The send task of the new stream should now succeed.
            await sendTask;
        }

        [Test]
        public void MultiplexedStreamFactory_SendAsync_Failure()
        {
            IMultiplexedStream stream = ClientMultiplexedStreamFactory.CreateStream(false);
            ClientConnection.Dispose();
            Assert.CatchAsync<TransportException>(
                async () => await stream.WriteAsync(CreateSendPayload(stream), true, default));
        }

        [Test]
        public async Task MultiplexedStreamFactory_SendAsync_FailureAsync()
        {
            IMultiplexedStream stream = ClientMultiplexedStreamFactory.CreateStream(true);
            await stream.WriteAsync(CreateSendPayload(stream), true, default);

            IMultiplexedStream serverStream = await ServerMultiplexedStreamFactory.AcceptStreamAsync(default);
            await serverStream.ReadAsync(CreateReceivePayload(), default);
            ServerConnection.Dispose();
            Assert.CatchAsync<StreamAbortedException>(
                async () => await serverStream.WriteAsync(CreateSendPayload(serverStream), true, default));
        }

        [SetUp]
        public Task SetUp() => SetUpConnectionsAsync();

        [TearDown]
        public void TearDown() => TearDownConnections();
    }
}
