// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    // Test the multi-stream interface.
    [Timeout(5000)]
    public class MultiplexedNetworkStreamFactoryTests : MultiplexedNetworkStreamFactoryBaseTest
    {
        private static readonly SlicOptions _serverSlicOptions = new()
            {
                BidirectionalStreamMaxCount = 15,
                UnidirectionalStreamMaxCount = 10
            };

        public MultiplexedNetworkStreamFactoryTests()
            : base(serverOptions: _serverSlicOptions)
        {
        }

        [Test]
        public void MultiplexedNetworkStreamFactory_Dispose()
        {
            ValueTask<IMultiplexedNetworkStream> acceptStreamTask = ServerMultiplexedNetworkStreamFactory.AcceptStreamAsync(default);
            ClientConnection.Close(new ConnectionClosedException());
            Assert.ThrowsAsync<ConnectionLostException>(async () => await acceptStreamTask);
        }

        [Test]
        public async Task MultiplexedNetworkStreamFactory_Dispose_StreamAbortedAsync()
        {
            IMultiplexedNetworkStream clientStream = ClientMultiplexedNetworkStreamFactory.CreateStream(true);
            await clientStream.WriteAsync(CreateSendPayload(clientStream), true, default);

            ClientConnection.Close(new ConnectionClosedException());

            StreamAbortedException? ex;
            // Stream is aborted
            ex = Assert.ThrowsAsync<StreamAbortedException>(
                async () => await clientStream.ReadAsync(CreateReceivePayload(), default));
            Assert.That(ex!.ErrorCode, Is.EqualTo(StreamError.ConnectionAborted));

            // Can't create new stream
            clientStream = ClientMultiplexedNetworkStreamFactory.CreateStream(true);
            Assert.ThrowsAsync<ConnectionClosedException>(
                async () => await clientStream.WriteAsync(CreateSendPayload(clientStream), true, default));
        }

        [Test]
        public async Task MultiplexedNetworkStreamFactory_AcceptStreamAsync()
        {
            IMultiplexedNetworkStream clientStream = ClientMultiplexedNetworkStreamFactory.CreateStream(bidirectional: true);
            ValueTask<IMultiplexedNetworkStream> acceptTask = ServerMultiplexedNetworkStreamFactory.AcceptStreamAsync(default);

            // The server-side won't accept the stream until the first frame is sent.
            await clientStream.WriteAsync(CreateSendPayload(clientStream), true, default);

            IMultiplexedNetworkStream serverStream = await acceptTask;

            Assert.That(serverStream.IsBidirectional, Is.True);
            Assert.AreEqual(serverStream.Id, clientStream.Id);
        }

        [Test]
        public void MultiplexedNetworkStreamFactory_AcceptStream_Cancellation()
        {
            using var source = new CancellationTokenSource();
            ValueTask<IMultiplexedNetworkStream> acceptTask = ServerMultiplexedNetworkStreamFactory.AcceptStreamAsync(source.Token);
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await acceptTask);
        }

        [Test]
        public void MultiplexedNetworkStreamFactory_AcceptStream_Failure()
        {
            ClientConnection.Close(new ConnectionClosedException());
            Assert.CatchAsync<TransportException>(async () => await ServerMultiplexedNetworkStreamFactory.AcceptStreamAsync(default));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiplexedNetworkStreamFactory_CreateStream(bool bidirectional)
        {
            IMultiplexedNetworkStream clientStream = ClientMultiplexedNetworkStreamFactory.CreateStream(bidirectional);
            Assert.Throws<InvalidOperationException>(() => _ = clientStream.Id); // stream is not started
            Assert.AreEqual(bidirectional, clientStream.IsBidirectional);

            await clientStream.WriteAsync(CreateSendPayload(clientStream), true, default);
            Assert.That(clientStream.Id, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task MultiplexedNetworkStreamFactory_StreamMaxCount_BidirectionalAsync()
        {
            var clientStreams = new List<IMultiplexedNetworkStream>();
            var serverStreams = new List<IMultiplexedNetworkStream>();
            for (int i = 0; i < _serverSlicOptions!.BidirectionalStreamMaxCount; ++i)
            {
                IMultiplexedNetworkStream stream = ClientMultiplexedNetworkStreamFactory.CreateStream(true);
                clientStreams.Add(stream);

                await stream.WriteAsync(CreateSendPayload(stream), true, default);

                serverStreams.Add(await ServerMultiplexedNetworkStreamFactory.AcceptStreamAsync(default));
                await serverStreams.Last().ReadAsync(CreateReceivePayload(), default);
            }

            // Ensure the client side accepts streams to receive data.
            ValueTask<IMultiplexedNetworkStream> acceptClientStream = ClientMultiplexedNetworkStreamFactory.AcceptStreamAsync(default);

            IMultiplexedNetworkStream clientStream = ClientMultiplexedNetworkStreamFactory.CreateStream(true);
            ValueTask sendTask = clientStream.WriteAsync(CreateSendPayload(clientStream), true, default);
            ValueTask<IMultiplexedNetworkStream> acceptTask = ServerMultiplexedNetworkStreamFactory.AcceptStreamAsync(default);

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
        public async Task MultiplexedNetworkStreamFactory_StreamMaxCount_StressTestAsync(bool bidirectional)
        {
            int maxCount = bidirectional ?
                _serverSlicOptions!.BidirectionalStreamMaxCount :
                _serverSlicOptions!.UnidirectionalStreamMaxCount;
            int streamCount = 0;

            // Ensure the client side accepts streams to receive payloads.
            _ = ClientMultiplexedNetworkStreamFactory.AcceptStreamAsync(default).AsTask();

            // Send many payloads and receive the payloads.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = SendAndReceiveAsync(ClientMultiplexedNetworkStreamFactory.CreateStream(bidirectional));
            }

            // Receive all the payloads and send the payloads.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = ReceiveAndSendAsync(await ServerMultiplexedNetworkStreamFactory.AcceptStreamAsync(default));
            }

            async Task SendAndReceiveAsync(IMultiplexedNetworkStream stream)
            {
                await stream.WriteAsync(CreateSendPayload(stream), true, default);

                Assert.That(Thread.VolatileRead(ref streamCount), Is.LessThanOrEqualTo(maxCount));

                if (bidirectional)
                {
                    await stream.ReadAsync(CreateReceivePayload(), default);
                }
            }

            async Task ReceiveAndSendAsync(IMultiplexedNetworkStream stream)
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
        public async Task MultiplexedNetworkStreamFactory_StreamMaxCount_UnidirectionalAsync()
        {
            var clientStreams = new List<IMultiplexedNetworkStream>();
            for (int i = 0; i < _serverSlicOptions!.UnidirectionalStreamMaxCount; ++i)
            {
                IMultiplexedNetworkStream stream = ClientMultiplexedNetworkStreamFactory.CreateStream(false);
                clientStreams.Add(stream);
                await stream.WriteAsync(CreateSendPayload(stream), true, default);
            }

            // Ensure the client side accepts streams to receive acknowledgement of stream completion.
            ValueTask<IMultiplexedNetworkStream> acceptClientStream = ClientMultiplexedNetworkStreamFactory.AcceptStreamAsync(default);

            IMultiplexedNetworkStream clientStream = ClientMultiplexedNetworkStreamFactory.CreateStream(false);
            ValueTask sendTask = clientStream.WriteAsync(CreateSendPayload(clientStream), true, default);

            // Accept a new unidirectional stream. This shouldn't allow the new stream send to complete since
            // the payload wasn't read yet on the stream.
            IMultiplexedNetworkStream serverStream = await ServerMultiplexedNetworkStreamFactory.AcceptStreamAsync(default);

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
        public void MultiplexedNetworkStreamFactory_SendAsync_Failure()
        {
            IMultiplexedNetworkStream stream = ClientMultiplexedNetworkStreamFactory.CreateStream(false);
            ClientConnection.Close(new ConnectionClosedException());
            Assert.CatchAsync<TransportException>(
                async () => await stream.WriteAsync(CreateSendPayload(stream), true, default));
        }

        [Test]
        public async Task MultiplexedNetworkStreamFactory_SendAsync_FailureAsync()
        {
            IMultiplexedNetworkStream stream = ClientMultiplexedNetworkStreamFactory.CreateStream(true);
            await stream.WriteAsync(CreateSendPayload(stream), true, default);

            IMultiplexedNetworkStream serverStream = await ServerMultiplexedNetworkStreamFactory.AcceptStreamAsync(default);
            await serverStream.ReadAsync(CreateReceivePayload(), default);
            ServerConnection.Close(new ConnectionClosedException());
            Assert.CatchAsync<StreamAbortedException>(
                async () => await serverStream.WriteAsync(CreateSendPayload(serverStream), true, default));
        }

        [SetUp]
        public Task SetUp() => SetUpConnectionsAsync();

        [TearDown]
        public void TearDown() => TearDownConnections();
    }
}
