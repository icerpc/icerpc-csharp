// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    // Test the multi-stream interface.
    [Timeout(5000)]
    public class MultiStreamConnectionTests : MultiStreamConnectionBaseTest
    {
        public MultiStreamConnectionTests()
            : base(bidirectionalStreamMaxCount: 15, unidirectionalStreamMaxCount: 10)
        {
        }

        [Test]
        public void MultiStreamConnection_Dispose()
        {
            ValueTask<INetworkStream> acceptStreamTask = ServerMultiStreamConnection.AcceptStreamAsync(default);
            ClientConnection.Close(new ConnectionClosedException());
            Assert.ThrowsAsync<ConnectionLostException>(async () => await acceptStreamTask);
        }

        // [Test]
        // public void MultiStreamConnection_Dispose_EmptyStreams()
        // {
        //     ClientConnection.Close(new ConnectionClosedException());
        //     ServerConnection.Close(new ConnectionClosedException());

        //     (long clientBidirectional, long clientUnidirectional) = ClientConnection.Shutdown();
        //     (long serverBidirectional, long serverUnidirectional) = ServerConnection.Shutdown();

        //     Assert.AreEqual(-1, clientBidirectional);
        //     Assert.AreEqual(-1, clientUnidirectional);
        //     Assert.AreEqual(-1, serverBidirectional);
        //     Assert.AreEqual(-1, serverUnidirectional);
        // }

        [Test]
        public async Task MultiStreamConnection_Dispose_StreamAbortedAsync()
        {
            INetworkStream clientStream = ClientMultiStreamConnection.CreateStream(true);
            await clientStream.SendAsync(CreateSendPayload(clientStream), true, default);

            ClientConnection.Close(new ConnectionClosedException());

            StreamAbortedException? ex;
            // Stream is aborted
            ex = Assert.ThrowsAsync<StreamAbortedException>(
                async () => await clientStream.ReceiveAsync(CreateReceivePayload(), default));
            Assert.That(ex!.ErrorCode, Is.EqualTo(StreamError.ConnectionAborted));

            // Can't create new stream
            clientStream = ClientMultiStreamConnection.CreateStream(true);
            Assert.ThrowsAsync<ConnectionClosedException>(
                async () => await clientStream.SendAsync(CreateSendPayload(clientStream), true, default));
        }

        // [Test]
        // public async Task MultiStreamConnection_LargestStreamIdsAsync()
        // {
        //     INetworkStream clientStream = ClientConnection.CreateStream(true);
        //     await clientStream.SendAsync(CreateSendPayload(clientStream), true, default);

        //     INetworkStream serverStream = await ServerConnection.AcceptStreamAsync(default);
        //     await serverStream.ReceiveAsync(CreateReceivePayload(), default);

        //     await serverStream.SendAsync(CreateSendPayload(serverStream), true, default);

        //     _ = ClientConnection.AcceptStreamAsync(default).AsTask();
        //     await clientStream.ReceiveAsync(CreateReceivePayload(), default);

        //     clientStream = ClientConnection.CreateStream(true);
        //     await clientStream.SendAsync(CreateSendPayload(clientStream), true, default);

        //     serverStream = await ServerConnection.AcceptStreamAsync(default);
        //     await serverStream.ReceiveAsync(CreateReceivePayload(), default);

        //     (long clientBidirectional, long _) = ClientConnection.Shutdown();
        //     (long serverBidirectional, long _) = ServerConnection.Shutdown();

        //     // Check that largest stream IDs are correct
        //     Assert.AreEqual(-1, clientBidirectional);
        //     Assert.AreEqual(4, serverBidirectional);

        //     // Terminate the streams
        //     await serverStream.SendAsync(CreateSendPayload(serverStream), true, default);
        //     await clientStream.ReceiveAsync(CreateReceivePayload(), default);

        //     Assert.AreEqual(0, ClientConnection.OutgoingStreamCount);
        //     Assert.AreEqual(0, ServerConnection.IncomingStreamCount);
        // }

        [Test]
        public async Task MultiStreamConnection_AcceptStreamAsync()
        {
            INetworkStream clientStream = ClientMultiStreamConnection.CreateStream(bidirectional: true);
            ValueTask<INetworkStream> acceptTask = ServerMultiStreamConnection.AcceptStreamAsync(default);

            // The server-side won't accept the stream until the first frame is sent.
            await clientStream.SendAsync(CreateSendPayload(clientStream), true, default);

            INetworkStream serverStream = await acceptTask;

            Assert.That(serverStream.IsBidirectional, Is.True);
            Assert.AreEqual(serverStream.Id, clientStream.Id);
        }

        [Test]
        public void MultiStreamConnection_AcceptStream_Cancellation()
        {
            using var source = new CancellationTokenSource();
            ValueTask<INetworkStream> acceptTask = ServerMultiStreamConnection.AcceptStreamAsync(source.Token);
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await acceptTask);
        }

        [Test]
        public void MultiStreamConnection_AcceptStream_Failure()
        {
            ClientConnection.Close(new ConnectionClosedException());
            Assert.CatchAsync<TransportException>(async () => await ServerMultiStreamConnection.AcceptStreamAsync(default));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiStreamConnection_CreateStream(bool bidirectional)
        {
            INetworkStream clientStream = ClientMultiStreamConnection.CreateStream(bidirectional);
            Assert.Throws<InvalidOperationException>(() => _ = clientStream.Id); // stream is not started
            Assert.AreEqual(bidirectional, clientStream.IsBidirectional);

            await clientStream.SendAsync(CreateSendPayload(clientStream), true, default);
            Assert.That(clientStream.Id, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task MultiStreamConnection_StreamMaxCount_BidirectionalAsync()
        {
            var clientStreams = new List<INetworkStream>();
            var serverStreams = new List<INetworkStream>();
            for (int i = 0; i < ServerSlicOptions!.BidirectionalStreamMaxCount; ++i)
            {
                INetworkStream stream = ClientMultiStreamConnection.CreateStream(true);
                clientStreams.Add(stream);

                await stream.SendAsync(CreateSendPayload(stream), true, default);

                serverStreams.Add(await ServerMultiStreamConnection.AcceptStreamAsync(default));
                await serverStreams.Last().ReceiveAsync(CreateReceivePayload(), default);
            }

            // Ensure the client side accepts streams to receive data.
            ValueTask<INetworkStream> acceptClientStream = ClientMultiStreamConnection.AcceptStreamAsync(default);

            INetworkStream clientStream = ClientMultiStreamConnection.CreateStream(true);
            ValueTask sendTask = clientStream.SendAsync(CreateSendPayload(clientStream), true, default);
            ValueTask<INetworkStream> acceptTask = ServerMultiStreamConnection.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened. The stream isn't opened
            // on the client side until we have confirmation from the server that we can open a new stream, so
            // the send should not complete.
            Assert.That(sendTask.IsCompleted, Is.False);
            Assert.That(acceptTask.IsCompleted, Is.False);

            // Close one stream by sending EOS after receiving the payload.
            await serverStreams.Last().SendAsync(CreateSendPayload(serverStreams.Last()), true, default);
            await clientStreams.Last().ReceiveAsync(CreateReceivePayload(), default);
            Assert.That(acceptClientStream.IsCompleted, Is.False);

            // Now it should be possible to accept the new stream on the server side.
            await sendTask;
            _ = await acceptTask;
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiStreamConnection_StreamMaxCount_StressTestAsync(bool bidirectional)
        {
            int maxCount = bidirectional ?
                ServerSlicOptions!.BidirectionalStreamMaxCount :
                ServerSlicOptions!.UnidirectionalStreamMaxCount;
            int streamCount = 0;

            // Ensure the client side accepts streams to receive payloads.
            _ = ClientMultiStreamConnection.AcceptStreamAsync(default).AsTask();

            // Send many payloads and receive the payloads.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = SendAndReceiveAsync(ClientMultiStreamConnection.CreateStream(bidirectional));
            }

            // Receive all the payloads and send the payloads.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = ReceiveAndSendAsync(await ServerMultiStreamConnection.AcceptStreamAsync(default));
            }

            async Task SendAndReceiveAsync(INetworkStream stream)
            {
                await stream.SendAsync(CreateSendPayload(stream), true, default);

                Assert.That(Thread.VolatileRead(ref streamCount), Is.LessThanOrEqualTo(maxCount));

                if (bidirectional)
                {
                    await stream.ReceiveAsync(CreateReceivePayload(), default);
                }
            }

            async Task ReceiveAndSendAsync(INetworkStream stream)
            {
                // Make sure the connection didn't accept more streams than it is allowed to.
                Assert.That(Thread.VolatileRead(ref streamCount), Is.LessThanOrEqualTo(maxCount));

                if (!bidirectional)
                {
                    // The stream is terminated as soon as the last frame of the request is received, so we have
                    // to decrement the count here before the request receive completes.
                    Interlocked.Decrement(ref streamCount);
                }

                _ = await stream.ReceiveAsync(CreateReceivePayload(), default);

                if (bidirectional)
                {
                    Interlocked.Decrement(ref streamCount);
                }

                if (bidirectional)
                {
                    await stream.SendAsync(CreateSendPayload(stream), true, default);
                }
            }
        }

        [Test]
        public async Task MultiStreamConnection_StreamMaxCount_UnidirectionalAsync()
        {
            var clientStreams = new List<INetworkStream>();
            for (int i = 0; i < ServerSlicOptions!.UnidirectionalStreamMaxCount; ++i)
            {
                INetworkStream stream = ClientMultiStreamConnection.CreateStream(false);
                clientStreams.Add(stream);
                await stream.SendAsync(CreateSendPayload(stream), true, default);
            }

            // Ensure the client side accepts streams to receive acknowledgement of stream completion.
            ValueTask<INetworkStream> acceptClientStream = ClientMultiStreamConnection.AcceptStreamAsync(default);

            INetworkStream clientStream = ClientMultiStreamConnection.CreateStream(false);
            ValueTask sendTask = clientStream.SendAsync(CreateSendPayload(clientStream), true, default);

            // Accept a new unidirectional stream. This shouldn't allow the new stream send to complete since
            // the payload wasn't read yet on the stream.
            INetworkStream serverStream = await ServerMultiStreamConnection.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened.The stream isn't opened
            // on the client side until we have confirmation from the server that we can open a new stream, so
            // the send should not complete.
            Assert.That(sendTask.IsCompleted, Is.False);

            // Close the server-side stream by receiving the payload from the stream.
            await serverStream.ReceiveAsync(CreateReceivePayload(), default);

            Assert.That(acceptClientStream.IsCompleted, Is.False);

            // The send task of the new stream should now succeed.
            await sendTask;
        }

        // TODO: XXX move to protocol tests

        // [Test]
        // public void MultiStreamConnection_PeerIncomingFrameMaxSize()
        // {
        //     // PeerIncomingFrameMaxSize is set when control streams are initialized in Setup()
        //     Assert.AreEqual(ClientConnection.IncomingFrameMaxSize, ServerConnection.PeerIncomingFrameMaxSize!.Value);
        //     Assert.AreEqual(ServerConnection.IncomingFrameMaxSize, ClientConnection.PeerIncomingFrameMaxSize!.Value);
        // }

        // [Test]
        // public async Task MultiStreamConnection_PingAsync()
        // {
        //     using var semaphore = new SemaphoreSlim(0);
        //     ServerConnection.PingReceived = () => semaphore.Release();
        //     using var source = new CancellationTokenSource();

        //     // Start accept stream on the server side to receive transport frames.
        //     ValueTask<INetworkStream> acceptStreamTask = ServerConnection.AcceptStreamAsync(source.Token);

        //     await ClientConnection.PingAsync(default);
        //     await semaphore.WaitAsync();

        //     await ClientConnection.PingAsync(default);
        //     await ClientConnection.PingAsync(default);
        //     await ClientConnection.PingAsync(default);
        //     await semaphore.WaitAsync();
        //     await semaphore.WaitAsync();
        //     await semaphore.WaitAsync();

        //     // Cancel AcceptStreamAsync
        //     source.Cancel();
        //     Assert.CatchAsync<OperationCanceledException>(async () => await acceptStreamTask);
        // }

        // [Test]
        // public void MultiStreamConnection_Ping_Cancellation()
        // {
        //     using var source = new CancellationTokenSource();
        //     source.Cancel();
        //     Assert.ThrowsAsync<OperationCanceledException>(async () => await ClientConnection.PingAsync(source.Token));
        // }

        // [Test]
        // public void MultiStreamConnection_Ping_Failure()
        // {
        //     ClientConnection.Close(new ConnectionClosedException());
        //     Assert.CatchAsync<TransportException>(async () => await ClientConnection.PingAsync(default));
        // }

        [Test]
        public void MultiStreamConnection_SendAsync_Failure()
        {
            INetworkStream stream = ClientMultiStreamConnection.CreateStream(false);
            ClientConnection.Close(new ConnectionClosedException());
            Assert.CatchAsync<TransportException>(
                async () => await stream.SendAsync(CreateSendPayload(stream), true, default));
        }

        [Test]
        public async Task MultiStreamConnection_SendAsync_FailureAsync()
        {
            INetworkStream stream = ClientMultiStreamConnection.CreateStream(true);
            await stream.SendAsync(CreateSendPayload(stream), true, default);

            INetworkStream serverStream = await ServerMultiStreamConnection.AcceptStreamAsync(default);
            await serverStream.ReceiveAsync(CreateReceivePayload(), default);
            ServerConnection.Close(new ConnectionClosedException());
            Assert.CatchAsync<StreamAbortedException>(
                async () => await serverStream.SendAsync(CreateSendPayload(serverStream), true, default));
        }

        [SetUp]
        public Task SetUp() => SetUpConnectionsAsync();

        [TearDown]
        public void TearDown() => TearDownConnections();
    }
}
