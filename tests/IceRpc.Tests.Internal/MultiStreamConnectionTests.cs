// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    // Test the varions multi-stream connection implementations.
    [Timeout(30000)]
    [TestFixture(MultiStreamConnectionType.Slic)]
    [TestFixture(MultiStreamConnectionType.Coloc)]
    public class MultiStreamConnectionTests : MultiStreamConnectionBaseTest
    {
        public MultiStreamConnectionTests(MultiStreamConnectionType type)
            : base(type, bidirectionalStreamMaxCount: 15, unidirectionalStreamMaxCount: 10)
        {
        }

        [Test]
        public void MultiStreamConnection_Dispose()
        {
            ValueTask<INetworkStream> acceptStreamTask = ServerConnection.AcceptStreamAsync(default);
            ClientConnection.Dispose();
            Assert.ThrowsAsync<ConnectionLostException>(async () => await acceptStreamTask);
        }

        [Test]
        public void MultiStreamConnection_Dispose_EmptyStreams()
        {
            ClientConnection.Dispose();
            ServerConnection.Dispose();

            (long clientBidirectional, long clientUnidirectional) = ClientConnection.Shutdown();
            (long serverBidirectional, long serverUnidirectional) = ServerConnection.Shutdown();

            Assert.AreEqual(-1, clientBidirectional);
            Assert.AreEqual(-1, clientUnidirectional);
            Assert.AreEqual(-1, serverBidirectional);
            Assert.AreEqual(-1, serverUnidirectional);
        }

        [Test]
        public async Task MultiStreamConnection_Dispose_StreamAbortedAsync()
        {
            INetworkStream clientStream = ClientConnection.CreateStream(true);
            await clientStream.SendAsync(CreateSendPayload(clientStream), true, default);

            ClientConnection.Dispose();
            (long clientBidirectional, long clientUnidirectional) = ClientConnection.Shutdown();

            StreamAbortedException? ex;
            // Stream is aborted
            ex = Assert.ThrowsAsync<StreamAbortedException>(
                async () => await clientStream.ReceiveAsync(CreateReceivePayload(), default));
            Assert.That(ex!.ErrorCode, Is.EqualTo(StreamError.ConnectionAborted));

            // Can't create new stream
            clientStream = ClientConnection.CreateStream(true);
            Assert.ThrowsAsync<ConnectionClosedException>(
                async () => await clientStream.SendAsync(CreateSendPayload(clientStream), true, default));

            (long serverBidirectional, long serverUnidirectional) = ServerConnection.Shutdown();

            Assert.AreEqual(-1, clientBidirectional);
            Assert.AreEqual(-1, clientUnidirectional);
            Assert.AreEqual(-1, serverBidirectional);
            Assert.AreEqual(-1, serverUnidirectional);

            Assert.AreEqual(0, ClientConnection.OutgoingStreamCount);
            Assert.AreEqual(0, ServerConnection.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamConnection_AbortOutgoingStreams_NoAbortStreamAsync()
        {
            INetworkStream clientStream = ClientConnection.CreateStream(true);
            await clientStream.SendAsync(CreateSendPayload(clientStream), true, default);

            INetworkStream serverStream = await ServerConnection.AcceptStreamAsync(default);
            _ = await serverStream.ReceiveAsync(CreateReceivePayload(), default);

            await serverStream.SendAsync(CreateSendPayload(serverStream), true, default);

            ClientConnection.AbortOutgoingStreams(StreamError.ConnectionShutdown, (clientStream.Id, 0));

            // Stream is not aborted
            _ = ClientConnection.AcceptStreamAsync(default).AsTask();
            await clientStream.ReceiveAsync(CreateReceivePayload(), default);

            (long serverBidirectional, long _) = ServerConnection.Shutdown();

            Assert.AreEqual(0, serverBidirectional);

            Assert.AreEqual(0, ClientConnection.OutgoingStreamCount);
            Assert.AreEqual(0, ServerConnection.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamConnection_LargestStreamIdsAsync()
        {
            INetworkStream clientStream = ClientConnection.CreateStream(true);
            await clientStream.SendAsync(CreateSendPayload(clientStream), true, default);

            INetworkStream serverStream = await ServerConnection.AcceptStreamAsync(default);
            await serverStream.ReceiveAsync(CreateReceivePayload(), default);

            await serverStream.SendAsync(CreateSendPayload(serverStream), true, default);

            _ = ClientConnection.AcceptStreamAsync(default).AsTask();
            await clientStream.ReceiveAsync(CreateReceivePayload(), default);

            clientStream = ClientConnection.CreateStream(true);
            await clientStream.SendAsync(CreateSendPayload(clientStream), true, default);

            serverStream = await ServerConnection.AcceptStreamAsync(default);
            await serverStream.ReceiveAsync(CreateReceivePayload(), default);

            (long clientBidirectional, long _) = ClientConnection.Shutdown();
            (long serverBidirectional, long _) = ServerConnection.Shutdown();

            // Check that largest stream IDs are correct
            Assert.AreEqual(-1, clientBidirectional);
            Assert.AreEqual(4, serverBidirectional);

            // Terminate the streams
            await serverStream.SendAsync(CreateSendPayload(serverStream), true, default);
            await clientStream.ReceiveAsync(CreateReceivePayload(), default);

            Assert.AreEqual(0, ClientConnection.OutgoingStreamCount);
            Assert.AreEqual(0, ServerConnection.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamConnection_AcceptStreamAsync()
        {
            INetworkStream clientStream = ClientConnection.CreateStream(bidirectional: true);
            ValueTask<INetworkStream> acceptTask = ServerConnection.AcceptStreamAsync(default);

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
            ValueTask<INetworkStream> acceptTask = ServerConnection.AcceptStreamAsync(source.Token);
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await acceptTask);
        }

        [Test]
        public void MultiStreamConnection_AcceptStream_Failure()
        {
            ClientConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ServerConnection.AcceptStreamAsync(default));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiStreamConnection_CreateStream(bool bidirectional)
        {
            INetworkStream clientStream = ClientConnection.CreateStream(bidirectional);
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
            for (int i = 0; i < ServerMultiStreamOptions!.BidirectionalStreamMaxCount; ++i)
            {
                INetworkStream stream = ClientConnection.CreateStream(true);
                clientStreams.Add(stream);

                await stream.SendAsync(CreateSendPayload(stream), true, default);

                serverStreams.Add(await ServerConnection.AcceptStreamAsync(default));
                await serverStreams.Last().ReceiveAsync(CreateReceivePayload(), default);
            }

            // Ensure the client side accepts streams to receive data.
            ValueTask<INetworkStream> acceptClientStream = ClientConnection.AcceptStreamAsync(default);

            INetworkStream clientStream = ClientConnection.CreateStream(true);
            ValueTask sendTask = clientStream.SendAsync(CreateSendPayload(clientStream), true, default);
            ValueTask<INetworkStream> acceptTask = ServerConnection.AcceptStreamAsync(default);

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
                ServerMultiStreamOptions!.BidirectionalStreamMaxCount :
                ServerMultiStreamOptions!.UnidirectionalStreamMaxCount;
            int streamCount = 0;

            // Ensure the client side accepts streams to receive payloads.
            _ = ClientConnection.AcceptStreamAsync(default).AsTask();

            // Send many payloads and receive the payloads.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = SendAndReceiveAsync(ClientConnection.CreateStream(bidirectional));
            }

            // Receive all the payloads and send the payloads.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = ReceiveAndSendAsync(await ServerConnection.AcceptStreamAsync(default));
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
            for (int i = 0; i < ServerMultiStreamOptions!.UnidirectionalStreamMaxCount; ++i)
            {
                INetworkStream stream = ClientConnection.CreateStream(false);
                clientStreams.Add(stream);
                await stream.SendAsync(CreateSendPayload(stream), true, default);
            }

            // Ensure the client side accepts streams to receive acknowledgement of stream completion.
            ValueTask<INetworkStream> acceptClientStream = ClientConnection.AcceptStreamAsync(default);

            INetworkStream clientStream = ClientConnection.CreateStream(false);
            ValueTask sendTask = clientStream.SendAsync(CreateSendPayload(clientStream), true, default);

            // Accept a new unidirectional stream. This shouldn't allow the new stream send to complete since
            // the payload wasn't read yet on the stream.
            INetworkStream serverStream = await ServerConnection.AcceptStreamAsync(default);

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

        // TODO: move to protocol tests
        // [Test]
        // public void MultiStreamConnection_PeerIncomingFrameMaxSize()
        // {
        //     // PeerIncomingFrameMaxSize is set when control streams are initialized in Setup()
        //     Assert.AreEqual(ClientConnection.IncomingFrameMaxSize, ServerConnection.PeerIncomingFrameMaxSize!.Value);
        //     Assert.AreEqual(ServerConnection.IncomingFrameMaxSize, ClientConnection.PeerIncomingFrameMaxSize!.Value);
        // }

        [Test]
        public async Task MultiStreamConnection_PingAsync()
        {
            using var semaphore = new SemaphoreSlim(0);
            ServerConnection.PingReceived = () => semaphore.Release();
            using var source = new CancellationTokenSource();

            // Start accept stream on the server side to receive transport frames.
            ValueTask<INetworkStream> acceptStreamTask = ServerConnection.AcceptStreamAsync(source.Token);

            await ClientConnection.PingAsync(default);
            await semaphore.WaitAsync();

            await ClientConnection.PingAsync(default);
            await ClientConnection.PingAsync(default);
            await ClientConnection.PingAsync(default);
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();

            // Cancel AcceptStreamAsync
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await acceptStreamTask);
        }

        [Test]
        public void MultiStreamConnection_Ping_Cancellation()
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await ClientConnection.PingAsync(source.Token));
        }

        [Test]
        public void MultiStreamConnection_Ping_Failure()
        {
            ClientConnection.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientConnection.PingAsync(default));
        }

        [Test]
        public void MultiStreamConnection_Properties()
        {
            Test(ClientConnection);
            Test(ServerConnection);

            Assert.That(ClientConnection.IsServer, Is.False);
            Assert.That(ServerConnection.IsServer, Is.True);

            static void Test(MultiStreamConnection connection)
            {
                Assert.That(connection.ToString(), Is.Not.Empty);
                Assert.AreNotEqual(connection.LastActivity, TimeSpan.Zero);
                Assert.AreEqual(0, connection.IncomingStreamCount);
                Assert.AreEqual(0, connection.OutgoingStreamCount);
            }
        }

        [Test]
        public void MultiStreamConnection_SendRequest_Failure()
        {
            INetworkStream stream = ClientConnection.CreateStream(false);
            ClientConnection.Dispose();
            Assert.CatchAsync<TransportException>(
                async () => await stream.SendAsync(CreateSendPayload(stream), true, default));
        }

        [Test]
        public async Task MultiStreamConnection_SendAsync_FailureAsync()
        {
            INetworkStream stream = ClientConnection.CreateStream(true);
            await stream.SendAsync(CreateSendPayload(stream), true, default);

            INetworkStream serverStream = await ServerConnection.AcceptStreamAsync(default);
            await serverStream.ReceiveAsync(CreateReceivePayload(), default);
            ServerConnection.Dispose();
            Assert.CatchAsync<StreamAbortedException>(
                async () => await serverStream.SendAsync(CreateSendPayload(serverStream), true, default));
        }

        [Order(1)]
        [Test]
        public async Task MultiStreamConnection_StreamCountAsync()
        {
            Assert.AreEqual(0, ClientConnection.IncomingStreamCount);
            Assert.AreEqual(0, ClientConnection.OutgoingStreamCount);
            Assert.AreEqual(0, ServerConnection.IncomingStreamCount);
            Assert.AreEqual(0, ServerConnection.OutgoingStreamCount);

            _ = ClientConnection.AcceptStreamAsync(default).AsTask();

            Func<ValueTask> release1 = await TestAsync(ClientConnection, ServerConnection, 1);
            Func<ValueTask> release2 = await TestAsync(ClientConnection, ServerConnection, 2);

            await release2();
            await release1();

            async Task<Func<ValueTask>> TestAsync(
                MultiStreamConnection connection,
                MultiStreamConnection peerConnection,
                int expectedCount)
            {
                INetworkStream clientStream = connection.CreateStream(true);
                Assert.AreEqual(expectedCount - 1, connection.OutgoingStreamCount);
                await clientStream.SendAsync(CreateSendPayload(clientStream), true, default);
                Assert.AreEqual(expectedCount, connection.OutgoingStreamCount);

                Assert.AreEqual(expectedCount - 1, peerConnection.IncomingStreamCount);
                INetworkStream serverStream = await ServerConnection.AcceptStreamAsync(default);
                Assert.AreEqual(expectedCount, peerConnection.IncomingStreamCount);

                await serverStream.ReceiveAsync(CreateReceivePayload(), default);

                return async () =>
                {
                    // Releases the stream by sending EOS.
                    await serverStream.SendAsync(CreateSendPayload(serverStream), true, default);
                    _ = await clientStream.ReceiveAsync(CreateReceivePayload(), default);

                    Assert.AreEqual(expectedCount - 1, connection.OutgoingStreamCount);
                    Assert.AreEqual(expectedCount - 1, peerConnection.IncomingStreamCount);
                };
            }
        }

        [SetUp]
        public Task SetUp() => SetUpConnectionsAsync();

        [TearDown]
        public void TearDown() => TearDownConnections();
    }
}
