// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    // Test the varions multi-stream socket implementations.
    [Timeout(30000)]
    [TestFixture(MultiStreamSocketType.Slic)]
    [TestFixture(MultiStreamSocketType.Coloc)]
    [TestFixture(MultiStreamSocketType.Ice1)]
    public class MultiStreamSocketTests : MultiStreamSocketBaseTest
    {
        public MultiStreamSocketTests(MultiStreamSocketType type)
            : base(type)
        {
            ServerConnectionOptions.BidirectionalStreamMaxCount = 15;
            ServerConnectionOptions.UnidirectionalStreamMaxCount = 10;
            ServerConnectionOptions.IncomingFrameMaxSize = 512 * 1024;
        }

        [Test]
        public void MultiStreamSocket_Dispose()
        {
            ValueTask<Stream> acceptStreamTask = ServerSocket.AcceptStreamAsync(default);
            ClientSocket.Dispose();
            Assert.ThrowsAsync<ConnectionLostException>(async () => await acceptStreamTask);
        }

        [Test]
        public void MultiStreamSocket_AbortStreams_EmptyStreams()
        {
            ClientSocket.AbortStreams(StreamErrorCode.ConnectionAborted);
            ServerSocket.AbortStreams(StreamErrorCode.ConnectionAborted);

            (long clientBidirectional, long clientUnidirectional) = ClientSocket.Shutdown();
            (long serverBidirectional, long serverUnidirectional) = ServerSocket.Shutdown();

            Assert.AreEqual(-1, clientBidirectional);
            Assert.AreEqual(3, clientUnidirectional); // server control stream ID = 3
            Assert.AreEqual(-1, serverBidirectional);
            Assert.AreEqual(2, serverUnidirectional); // client control stream ID = 2
        }

        [Test]
        public async Task MultiStreamSocket_AbortStreams_AbortStreamAsync()
        {
            var clientStream = ClientSocket.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            ClientSocket.AbortStreams(StreamErrorCode.ConnectionAborted);
            (long clientBidirectional, long clientUnidirectional) = ClientSocket.Shutdown();

            StreamAbortedException? ex;
            // Stream is aborted
            ex = Assert.ThrowsAsync<StreamAbortedException>(
                async () => await clientStream.ReceiveResponseFrameAsync(default));
            Assert.That(ex!.ErrorCode, Is.EqualTo(StreamErrorCode.ConnectionAborted));
            clientStream.Release();

            // Can't create new stream
            clientStream = ClientSocket.CreateStream(true);
            Assert.ThrowsAsync<ConnectionClosedException>(
                async () => await clientStream.SendRequestFrameAsync(DummyRequest));

            (long serverBidirectional, long serverUnidirectional) = ServerSocket.Shutdown();

            clientStream.Release();

            Assert.AreEqual(-1, clientBidirectional);
            Assert.AreEqual(3, clientUnidirectional); // server control stream ID = 3
            Assert.AreEqual(-1, serverBidirectional);
            Assert.AreEqual(2, serverUnidirectional); // client control stream ID = 2

            Assert.AreEqual(0, ClientSocket.OutgoingStreamCount);
            Assert.AreEqual(0, ServerSocket.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamSocket_AbortStreams_NoAbortStreamAsync()
        {
            var ex = new InvalidOperationException();

            var clientStream = ClientSocket.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            var serverStream = await ServerSocket.AcceptStreamAsync(default);
            var incomingRequest = await serverStream.ReceiveRequestFrameAsync(default);

            await serverStream.SendResponseFrameAsync(GetResponseFrame(incomingRequest));

            ClientSocket.AbortOutgoingStreams(StreamErrorCode.ConnectionShutdown, (clientStream.Id, 0));

            // Stream is not aborted
            _ = ClientSocket.AcceptStreamAsync(default).AsTask();
            await clientStream.ReceiveResponseFrameAsync(default);

            (long serverBidirectional, long _) = ServerSocket.Shutdown();

            clientStream.Release();
            serverStream.Release();

            Assert.AreEqual(0, serverBidirectional);

            Assert.AreEqual(0, ClientSocket.OutgoingStreamCount);
            Assert.AreEqual(0, ServerSocket.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamSocket_AbortStreams_LargestStreamIdsAsync()
        {
            var ex = new InvalidOperationException();

            var clientStream = ClientSocket.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            var serverStream = await ServerSocket.AcceptStreamAsync(default);
            var incomingRequest = await serverStream.ReceiveRequestFrameAsync(default);

            await serverStream.SendResponseFrameAsync(
                new OutgoingResponse(incomingRequest, new UnhandledException(ex)),
                default);

            _ = ClientSocket.AcceptStreamAsync(default).AsTask();
            await clientStream.ReceiveResponseFrameAsync(default);

            clientStream.Release();
            serverStream.Release();

            clientStream = ClientSocket.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            serverStream = await ServerSocket.AcceptStreamAsync(default);
            await serverStream.ReceiveRequestFrameAsync();

            (long clientBidirectional, long _) = ClientSocket.Shutdown();
            (long serverBidirectional, long _) = ServerSocket.Shutdown();

            clientStream.Release();
            serverStream.Release();

            // Check that largest stream IDs are correct
            Assert.AreEqual(-1, clientBidirectional);
            Assert.AreEqual(4, serverBidirectional);

            Assert.AreEqual(0, ClientSocket.OutgoingStreamCount);
            Assert.AreEqual(0, ServerSocket.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamSocket_AcceptStreamAsync()
        {
            Stream clientStream = ClientSocket.CreateStream(bidirectional: true);
            ValueTask<Stream> acceptTask = ServerSocket.AcceptStreamAsync(default);

            // The server-side won't accept the stream until the first frame is sent.
            await clientStream.SendRequestFrameAsync(DummyRequest);

            Stream serverStream = await acceptTask;

            Assert.IsTrue(serverStream.IsBidirectional);
            Assert.IsTrue(serverStream.IsStarted);
            Assert.IsFalse(serverStream.IsControl);
            Assert.AreEqual(serverStream.Id, clientStream.Id);

            clientStream.Release();
            serverStream.Release();
        }

        [Test]
        public void MultiStreamSocket_AcceptStream_Cancellation()
        {
            using var source = new CancellationTokenSource();
            ValueTask<Stream> acceptTask = ServerSocket.AcceptStreamAsync(source.Token);
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await acceptTask);
        }

        [Test]
        public void MultiStreamSocket_AcceptStream_Failure()
        {
            ClientSocket.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ServerSocket.AcceptStreamAsync(default));
        }

        [Test]
        public async Task MultiStreamSocket_CloseAsync_CancellationAsync()
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            try
            {
                // This will either complete successfully or with an OperationCanceledException depending on the
                // implementation (which might be a no-op).
                await ClientSocket.CloseAsync(0, source.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiStreamSocket_CreateStream(bool bidirectional)
        {
            Stream clientStream = ClientSocket.CreateStream(bidirectional);
            Assert.IsFalse(clientStream.IsStarted);
            Assert.Throws<InvalidOperationException>(() => _ = clientStream.Id); // stream is not started
            Assert.AreEqual(bidirectional, clientStream.IsBidirectional);
            Assert.IsFalse(clientStream.IsControl);

            await clientStream.SendRequestFrameAsync(DummyRequest);
            Assert.IsTrue(clientStream.IsStarted);
            Assert.GreaterOrEqual(clientStream.Id, 0);

            clientStream.Release();
        }

        [Test]
        public async Task MultiStreamSocket_StreamMaxCount_BidirectionalAsync()
        {
            var clientStreams = new List<Stream>();
            var serverStreams = new List<Stream>();
            IncomingRequest? incomingRequest = null;
            for (int i = 0; i < ServerConnectionOptions.BidirectionalStreamMaxCount; ++i)
            {
                var stream = ClientSocket.CreateStream(true);
                clientStreams.Add(stream);

                await stream.SendRequestFrameAsync(DummyRequest);

                serverStreams.Add(await ServerSocket.AcceptStreamAsync(default));
                var request = await serverStreams.Last().ReceiveRequestFrameAsync();
                incomingRequest ??= request;
            }

            // Ensure the client side accepts streams to receive responses.
            ValueTask<Stream> acceptClientStream = ClientSocket.AcceptStreamAsync(default);

            var clientStream = ClientSocket.CreateStream(true);
            ValueTask sendTask = clientStream.SendRequestFrameAsync(DummyRequest);
            ValueTask<Stream> acceptTask = ServerSocket.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened. For Ice1, the sending of the
            // request should succeed since the max count is only checked on the server side. For collocated and slic,
            // the stream isn't opened on the client side until we have confirmation from the server that we can open
            // a new stream, so the send shouldn't not complete.
            Assert.AreEqual(SocketType == MultiStreamSocketType.Ice1, sendTask.IsCompleted);
            Assert.IsFalse(acceptTask.IsCompleted);

            // Close one stream by sending the response (which sends the stream EOS) after receiving it.
            await serverStreams.Last().SendResponseFrameAsync(GetResponseFrame(incomingRequest!));
            await clientStreams.Last().ReceiveResponseFrameAsync();
            Assert.IsFalse(acceptClientStream.IsCompleted);
            clientStreams.Last().Release();
            serverStreams.Last().Release();

            // Now it should be possible to accept the new stream on the server side.
            await sendTask;
            var serverStream = await acceptTask;

            clientStream.Release();
            serverStream.Release();

            foreach (var stream in clientStreams)
            {
                stream.Release();
            }
            foreach (var stream in serverStreams)
            {
                stream.Release();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MultiStreamSocket_StreamMaxCount_StressTestAsync(bool bidirectional)
        {
            int maxCount = bidirectional ?
                ServerConnectionOptions.BidirectionalStreamMaxCount :
                ServerConnectionOptions.UnidirectionalStreamMaxCount;
            int streamCount = 0;

            // Ensure the client side accepts streams to receive responses.
            _ = ClientSocket.AcceptStreamAsync(default).AsTask();

            // Send many requests and receive the responses.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = SendRequestAndReceiveResponseAsync(ClientSocket.CreateStream(bidirectional));
            }

            // Receive all the requests and send the responses.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = ReceiveRequestAndSendResponseAsync(await ServerSocket.AcceptStreamAsync(default));
            }

            async Task SendRequestAndReceiveResponseAsync(Stream stream)
            {
                await stream.SendRequestFrameAsync(DummyRequest);

                // With non-Ice1 sockets, the client-side keeps track of the stream max count and it ensures that it
                // doesn't open more streams that the server permits.
                if (SocketType != MultiStreamSocketType.Ice1)
                {
                    Interlocked.Increment(ref streamCount);
                    Assert.LessOrEqual(Thread.VolatileRead(ref streamCount), maxCount);
                }
                if (bidirectional)
                {
                    await stream.ReceiveResponseFrameAsync();
                }
                stream.Release();
            }

            async Task ReceiveRequestAndSendResponseAsync(Stream stream)
            {
                // Ice1 stream max count is enforced on the server-side only. The stream is accepted only
                // the server-side stream count permits it.
                if (SocketType == MultiStreamSocketType.Ice1)
                {
                    Interlocked.Increment(ref streamCount);
                    Assert.LessOrEqual(Thread.VolatileRead(ref streamCount), maxCount);
                }

                var request = await stream.ReceiveRequestFrameAsync();

                // With non-Ice1 sockets, the server-side releases the stream shortly before sending the
                // last stream frame (with the response).
                if (SocketType != MultiStreamSocketType.Ice1)
                {
                    Assert.LessOrEqual(Thread.VolatileRead(ref streamCount), maxCount);
                    Interlocked.Decrement(ref streamCount);
                }

                if (bidirectional)
                {
                    await stream.SendResponseFrameAsync(GetResponseFrame(request));
                }

                if (SocketType == MultiStreamSocketType.Ice1)
                {
                    Assert.LessOrEqual(Thread.VolatileRead(ref streamCount), maxCount);
                    Interlocked.Decrement(ref streamCount);
                }
                stream.Release();
            }
        }

        [Test]
        public async Task MultiStreamSocket_StreamMaxCount_UnidirectionalAsync()
        {
            var clientStreams = new List<Stream>();
            var serverStreams = new List<Stream>();
            for (int i = 0; i < ServerConnectionOptions.UnidirectionalStreamMaxCount; ++i)
            {
                var stream = ClientSocket.CreateStream(false);
                clientStreams.Add(stream);
                await stream.SendRequestFrameAsync(DummyRequest);
                stream.Release();

                serverStreams.Add(await ServerSocket.AcceptStreamAsync(default));
                await serverStreams.Last().ReceiveRequestFrameAsync();
            }

            // Ensure the client side accepts streams to receive responses.
            ValueTask<Stream> acceptClientStream = ClientSocket.AcceptStreamAsync(default);

            var clientStream = ClientSocket.CreateStream(false);
            ValueTask sendTask = clientStream.SendRequestFrameAsync(DummyRequest);
            ValueTask<Stream> acceptTask = ServerSocket.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened. For Ice1, the sending of the
            // request should succeed since the max count is only checked on the server side. For collocated and slic,
            // the stream isn't opened on the client side until we have confirmation from the server that we can open
            // a new stream, so the send shouldn't not complete.
            Assert.AreEqual(SocketType == MultiStreamSocketType.Ice1, sendTask.IsCompleted);
            Assert.IsFalse(acceptTask.IsCompleted);

            // Close one stream by releasing the stream on the server-side.
            serverStreams.Last().Release();
            Assert.IsFalse(acceptClientStream.IsCompleted);

            // Now it should be possible to accept the new stream on the server side.
            await sendTask;
            var serverStream = await acceptTask;

            clientStream.Release();
            serverStream.Release();

            foreach (var stream in clientStreams)
            {
                stream.Release();
            }
            foreach (var stream in serverStreams)
            {
                stream.Release();
            }
        }

        [Test]
        public void MultiStreamSocket_PeerIncomingFrameMaxSize()
        {
            // PeerIncomingFrameMaxSize is set when control streams are initialized in Setup()
            if (SocketType == MultiStreamSocketType.Ice1)
            {
                Assert.IsNull(ServerSocket.PeerIncomingFrameMaxSize);
                Assert.IsNull(ClientSocket.PeerIncomingFrameMaxSize);
            }
            else
            {
                Assert.AreEqual(ClientSocket.IncomingFrameMaxSize, ServerSocket.PeerIncomingFrameMaxSize!.Value);
                Assert.AreEqual(ServerSocket.IncomingFrameMaxSize, ClientSocket.PeerIncomingFrameMaxSize!.Value);
            }
        }

        [Test]
        public async Task MultiStreamSocket_PingAsync()
        {
            var semaphore = new SemaphoreSlim(0);
            ServerSocket.PingReceived = () => semaphore.Release();
            using var source = new CancellationTokenSource();

            // Start accept stream on the server side to receive transport frames.
            var acceptStreamTask = ServerSocket.AcceptStreamAsync(source.Token);

            await ClientSocket.PingAsync(default);
            await semaphore.WaitAsync();

            await ClientSocket.PingAsync(default);
            await ClientSocket.PingAsync(default);
            await ClientSocket.PingAsync(default);
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();

            // Cancel AcceptStreamAsync
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await acceptStreamTask);
        }

        [Test]
        public void MultiStreamSocket_Ping_Cancellation()
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await ClientSocket.PingAsync(source.Token));
        }

        [Test]
        public void MultiStreamSocket_Ping_Failure()
        {
            ClientSocket.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.PingAsync(default));
        }

        [Test]
        public void MultiStreamSocket_Properties()
        {
            Test(ClientSocket);
            Test(ServerSocket);

            Assert.IsTrue(!ClientSocket.IsIncoming);
            Assert.IsTrue(ServerSocket.IsIncoming);

            static void Test(MultiStreamConnection socket)
            {
                Assert.NotNull(socket.LocalEndpoint != null);
                Assert.AreNotEqual(socket.IdleTimeout, TimeSpan.Zero);
                Assert.Greater(socket.IncomingFrameMaxSize, 0);
                if (socket.Protocol != Protocol.Ice1)
                {
                    Assert.Greater(socket.PeerIncomingFrameMaxSize, 0);
                }
                else
                {
                    Assert.IsNull(socket.PeerIncomingFrameMaxSize);
                }
                Assert.IsNotEmpty(socket.ToString());
                Assert.AreNotEqual(socket.LastActivity, TimeSpan.Zero);
                Assert.AreEqual(0, socket.LastResponseStreamId);
                Assert.AreEqual(0, socket.IncomingStreamCount);
                Assert.AreEqual(0, socket.OutgoingStreamCount);
            }

            Assert.AreEqual(512 * 1024, ServerSocket.IncomingFrameMaxSize);
            Assert.AreEqual(1024 * 1024, ClientSocket.IncomingFrameMaxSize);
        }

        [Test]
        public void MultiStreamSocket_SendRequest_Failure()
        {
            var stream = ClientSocket.CreateStream(false);
            ClientSocket.Dispose();
            Assert.CatchAsync<TransportException>(async () => await stream.SendRequestFrameAsync(DummyRequest));
        }

        [Test]
        public async Task MultiStreamSocket_SendResponse_FailureAsync()
        {
            var stream = ClientSocket.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);

            var serverStream = await ServerSocket.AcceptStreamAsync(default);
            var request = await serverStream.ReceiveRequestFrameAsync();
            ServerSocket.Dispose();
            Assert.CatchAsync<StreamAbortedException>(
                async () => await serverStream.SendResponseFrameAsync(GetResponseFrame(request)));
        }

        [Order(1)]
        public async Task MultiStreamSocket_StreamCountAsync()
        {
            Assert.AreEqual(0, ClientSocket.IncomingStreamCount);
            Assert.AreEqual(0, ClientSocket.OutgoingStreamCount);
            Assert.AreEqual(0, ServerSocket.IncomingStreamCount);
            Assert.AreEqual(0, ServerSocket.OutgoingStreamCount);

            var release1 = await TestAsync(ClientSocket, ServerSocket, 1);
            var release2 = await TestAsync(ServerSocket, ClientSocket, 1);

            var release3 = await TestAsync(ClientSocket, ServerSocket, 2);
            var release4 = await TestAsync(ServerSocket, ClientSocket, 2);

            release4();
            release3();

            release2();
            release1();

            async Task<Action> TestAsync(MultiStreamConnection socket, MultiStreamConnection peerSocket, int expectedCount)
            {
                var clientStream = socket.CreateStream(true);
                Assert.AreEqual(expectedCount - 1, socket.OutgoingStreamCount);
                ValueTask task = clientStream.SendRequestFrameAsync(DummyRequest);
                Assert.AreEqual(expectedCount, socket.OutgoingStreamCount);

                Assert.AreEqual(expectedCount - 1, peerSocket.IncomingStreamCount);
                var serverStream = await peerSocket.AcceptStreamAsync(default);
                Assert.AreEqual(expectedCount, peerSocket.IncomingStreamCount);

                await task;
                return () =>
                {
                    clientStream.Release();
                    Assert.AreEqual(expectedCount - 1, socket.OutgoingStreamCount);

                    serverStream.Release();
                    Assert.AreEqual(expectedCount - 1, peerSocket.IncomingStreamCount);
                };
            }
        }

        [SetUp]
        public Task SetUp() => SetUpSocketsAsync();

        [TearDown]
        public void TearDown() => TearDownSockets();
    }
}
