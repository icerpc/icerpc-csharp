// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    public enum MultiStreamSocketType
    {
        Ice1,
        Colocated,
        Slic
    }

    [Parallelizable(scope: ParallelScope.Fixtures)]
    public class MultiStreamSocketBaseTest : SocketBaseTest
    {
        protected OutgoingRequestFrame DummyRequest => OutgoingRequestFrame.WithEmptyArgs(Proxy, "foo", false);
        protected MultiStreamSocket ClientSocket => _clientSocket!;
        protected IServicePrx Proxy => _proxy!;
        protected MultiStreamSocket ServerSocket => _serverSocket!;
        protected MultiStreamSocketType SocketType { get; }
        private MultiStreamSocket? _clientSocket;
        private SocketStream? _controlStreamForClient;
        private SocketStream? _controlStreamForServer;
        private SocketStream? _peerControlStreamForClient;
        private SocketStream? _peerControlStreamForServer;
        private IServicePrx? _proxy;
        private MultiStreamSocket? _serverSocket;

        public MultiStreamSocketBaseTest(MultiStreamSocketType socketType)
            : base(socketType == MultiStreamSocketType.Ice1 ? Protocol.Ice1 : Protocol.Ice2,
                   socketType == MultiStreamSocketType.Colocated ? "colocated" : "tcp",
                   false) =>
            SocketType = socketType;

        public async Task SetUpSockets()
        {
            Task<MultiStreamSocket> acceptTask = AcceptAsync();
            (_clientSocket, _proxy) = await ConnectAndGetProxyAsync();
            _serverSocket = await acceptTask;

            ValueTask initializeTask = _serverSocket.InitializeAsync(default);
            await _clientSocket.InitializeAsync(default);
            await initializeTask;

            _controlStreamForClient = await ClientSocket.SendInitializeFrameAsync(default);
            _controlStreamForServer = await ServerSocket.SendInitializeFrameAsync(default);

            _peerControlStreamForClient = await ClientSocket.ReceiveInitializeFrameAsync(default);
            _peerControlStreamForServer = await ServerSocket.ReceiveInitializeFrameAsync(default);
        }

        public void TearDownSockets()
        {
            _controlStreamForClient?.Release();
            _peerControlStreamForClient?.Release();
            _controlStreamForServer?.Release();
            _peerControlStreamForServer?.Release();

            _clientSocket?.Dispose();
            _serverSocket?.Dispose();
        }

        static protected OutgoingResponseFrame GetResponseFrame(IncomingRequestFrame request) =>
            // TODO: Fix once OutgoingRespongFrame construction is simplified to not depend on Current
            new(request, new UnhandledException(new InvalidOperationException()));
    }

    // Test the varions multi-stream socket implementations.
    [Timeout(10000)]
    [TestFixture(MultiStreamSocketType.Slic)]
    [TestFixture(MultiStreamSocketType.Colocated)]
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
        public void MultiStreamSocket_Abort()
        {
            ValueTask<SocketStream> acceptStreamTask = ServerSocket.AcceptStreamAsync(default);
            ClientSocket.Abort();
            Assert.ThrowsAsync<ConnectionLostException>(async () => await acceptStreamTask);
        }

        [Test]
        public void MultiStreamSocket_AbortStreams_EmptyStreams()
        {
            var ex = new InvalidOperationException();
            ClientSocket.AbortStreams(ex);
            ServerSocket.AbortStreams(ex);

            (var clientBidirectional, var clientUnidirectional) = ClientSocket.AbortStreams(ex, Failure);
            (var serverBidirectional, var serverUnidirectional) = ServerSocket.AbortStreams(ex, Failure);

            Assert.AreEqual(0, clientBidirectional);
            Assert.AreEqual(3, clientUnidirectional); // server control stream ID = 3
            Assert.AreEqual(0, serverBidirectional);
            Assert.AreEqual(3, serverUnidirectional); // server control stream ID = 3

            static bool Failure(SocketStream stream)
            {
                Assert.Fail();
                return false;
            }
        }

        [Test]
        public async Task MultiStreamSocket_AbortStreams_AbortStream()
        {
            var ex = new InvalidOperationException();

            var clientStream = ClientSocket.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            (var clientBidirectional, var clientUnidirectional) = ClientSocket.AbortStreams(ex, stream => {
                Assert.AreEqual(stream, clientStream);
                return true; // Abort the stream
            });

            // Stream is aborted
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await clientStream.ReceiveResponseFrameAsync(default));
            clientStream.Release();

            // Can't create new stream
            clientStream = ClientSocket.CreateStream(true);
            Assert.ThrowsAsync<ConnectionClosedException>(
                async () => await clientStream.SendRequestFrameAsync(DummyRequest));

            // Abort stream because AcceptStreamAsync triggers CloseConnectionException when receiving the
            // request frame and creating the new stream.
            (var serverBidirectional, var serverUnidirectional) = ServerSocket.AbortStreams(ex);
            Assert.ThrowsAsync<ConnectionClosedException>(async () => await ServerSocket.AcceptStreamAsync(default));

            clientStream.Release();

            Assert.AreEqual(0, clientBidirectional);
            Assert.AreEqual(3, clientUnidirectional); // server control stream ID = 3
            Assert.AreEqual(0, serverBidirectional);
            Assert.AreEqual(3, serverUnidirectional); // server control stream ID = 3

            Assert.AreEqual(0, ClientSocket.OutgoingStreamCount);
            Assert.AreEqual(0, ServerSocket.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamSocket_AbortStreams_NoAbortStream()
        {
            var ex = new InvalidOperationException();

            var clientStream = ClientSocket.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            var serverStream = await ServerSocket.AcceptStreamAsync(default);
            var incomingRequest = await serverStream.ReceiveRequestFrameAsync(default);

            await serverStream.SendResponseFrameAsync(GetResponseFrame(incomingRequest));

            (var clientBidirectional, var clientUnidirectional) = ClientSocket.AbortStreams(ex, stream =>
            {
                Assert.AreEqual(stream, clientStream);
                return false; // Don't abort the stream
            });

            // Stream is not aborted
            var acceptTask = ClientSocket.AcceptStreamAsync(default);
            await clientStream.ReceiveResponseFrameAsync(default);

            (var serverBidirectional, var serverUnidirectional) = ServerSocket.AbortStreams(ex, stream =>
            {
                Assert.AreEqual(stream, serverStream);
                return false;
            });

            clientStream.Release();
            serverStream.Release();

            Assert.AreEqual(0, clientBidirectional);
            Assert.AreEqual(0, serverBidirectional);

            Assert.AreEqual(0, ClientSocket.OutgoingStreamCount);
            Assert.AreEqual(0, ServerSocket.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamSocket_AbortStreams_LargestStreamIds()
        {
            var ex = new InvalidOperationException();

            var clientStream = ClientSocket.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            var serverStream = await ServerSocket.AcceptStreamAsync(default);
            var incomingRequest = await serverStream.ReceiveRequestFrameAsync(default);

            await serverStream.SendResponseFrameAsync(
                new OutgoingResponseFrame(incomingRequest, new UnhandledException(ex)),
                default);

            var acceptTask = ClientSocket.AcceptStreamAsync(default);
            await clientStream.ReceiveResponseFrameAsync(default);

            clientStream.Release();
            serverStream.Release();

            clientStream = ClientSocket.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            serverStream = await ServerSocket.AcceptStreamAsync(default);
            await serverStream.ReceiveRequestFrameAsync();

            (var clientBidirectional, var clientUnidirectional) = ClientSocket.AbortStreams(ex, stream =>
            {
                Assert.AreEqual(stream, clientStream);
                return false;
            });
            (var serverBidirectional, var serverUnidirectional) = ServerSocket.AbortStreams(ex, stream =>
            {
                Assert.AreEqual(stream, serverStream);
                return false;
            });

            clientStream.Release();
            serverStream.Release();

            // Check that largest stream IDs are correct
            Assert.AreEqual(4, clientBidirectional);
            Assert.AreEqual(4, serverBidirectional);

            Assert.AreEqual(0, ClientSocket.OutgoingStreamCount);
            Assert.AreEqual(0, ServerSocket.IncomingStreamCount);
        }

        [Test]
        public async Task MultiStreamSocket_AcceptStream()
        {
            SocketStream clientStream = ClientSocket.CreateStream(bidirectional: true);
            ValueTask<SocketStream> acceptTask = ServerSocket.AcceptStreamAsync(default);

            // The server-side won't accept the stream until the first frame is sent.
            await clientStream.SendRequestFrameAsync(DummyRequest);

            SocketStream serverStream = await acceptTask;

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
            ValueTask<SocketStream> acceptTask = ServerSocket.AcceptStreamAsync(source.Token);
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await acceptTask);
        }

        [Test]
        public void MultiStreamSocket_AcceptStream_Failure()
        {
            ClientSocket.Abort();
            Assert.CatchAsync<TransportException>(async () => await ServerSocket.AcceptStreamAsync(default));
        }

        [Test]
        public async Task MultiStreamSocket_CloseAsync_Cancellation()
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            try
            {
                // This will either complete successfully or with an OperationCanceledException depending on the
                // implementation (which might be a no-op).
                await ClientSocket.CloseAsync(new InvalidDataException(""), source.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MultiStreamSocket_CreateStream(bool bidirectional)
        {
            var clientStream = ClientSocket.CreateStream(bidirectional);
            Assert.IsFalse(clientStream.IsStarted);
            Assert.Throws<InvalidOperationException>(() => _ = clientStream.Id); // stream is not started
            Assert.AreEqual(bidirectional, clientStream.IsBidirectional);
            Assert.IsFalse(clientStream.IsControl);

            ValueTask task = clientStream.SendRequestFrameAsync(DummyRequest);

            Assert.IsTrue(clientStream.IsStarted);
            Assert.GreaterOrEqual(clientStream.Id, 0);

            clientStream.Release();
        }

        [Test]
        public void MultiStreamSocket_Dispose()
        {
            ClientSocket.Dispose();
            ServerSocket.Dispose();
            ClientSocket.Dispose();
            ServerSocket.Dispose();
        }

        [Test]
        public async Task MultiStreamSocket_StreamMaxCount_Bidirectional()
        {
            var clientStreams = new List<SocketStream>();
            var serverStreams = new List<SocketStream>();
            IncomingRequestFrame? incomingRequest = null;
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
            ValueTask<SocketStream> acceptClientStream = ClientSocket.AcceptStreamAsync(default);

            var clientStream = ClientSocket.CreateStream(true);
            ValueTask sendTask = clientStream.SendRequestFrameAsync(DummyRequest);
            ValueTask<SocketStream> acceptTask = ServerSocket.AcceptStreamAsync(default);

            await Task.Delay(200);

            // New stream can't be accepted since max stream count are already opened. For Ice1, the sending of the
            // request should succeed since the max count is only checked on the server side. For collocated and slic,
            // the stream isn't opened on the client side until we have confirmation from the server that we can open
            // a new stream, so the send shouldn't not complete.
            Assert.AreEqual(SocketType == MultiStreamSocketType.Ice1, sendTask.IsCompleted);
            Assert.IsFalse(acceptTask.IsCompleted);

            // Close one stream by sending the response (which sends the stream EOS) and receiving it.
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
        public async Task MultiStreamSocket_StreamMaxCount_StressTest(bool bidirectional)
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
                _ = SendRequestAndReceiveResponse(ClientSocket.CreateStream(bidirectional));
            }

            // Receive all the requests and send the responses.
            for (int i = 0; i < 10 * maxCount; ++i)
            {
                _ = ReceiveRequestAndSendResponse(await ServerSocket.AcceptStreamAsync(default));
            }

            async Task SendRequestAndReceiveResponse(SocketStream stream)
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

            async Task ReceiveRequestAndSendResponse(SocketStream stream)
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
        public async Task MultiStreamSocket_StreamMaxCount_Unidirectional()
        {
            var clientStreams = new List<SocketStream>();
            var serverStreams = new List<SocketStream>();
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
            ValueTask<SocketStream> acceptClientStream = ClientSocket.AcceptStreamAsync(default);

            var clientStream = ClientSocket.CreateStream(false);
            ValueTask sendTask = clientStream.SendRequestFrameAsync(DummyRequest);
            ValueTask<SocketStream> acceptTask = ServerSocket.AcceptStreamAsync(default);

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
        public async Task MultiStreamSocket_Ping()
        {
            var semaphore = new SemaphoreSlim(0);
            ServerSocket.Ping += EventHandler;
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

            ServerSocket.Ping -= EventHandler;

            void EventHandler(object? state, EventArgs args) => semaphore.Release();
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
            ClientSocket.Abort();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.PingAsync(default));
        }

        [Test]
        public void MultiStreamSocket_Properties()
        {
            Test(ClientSocket);
            Test(ServerSocket);

            Assert.IsTrue(!ClientSocket.IsIncoming);
            Assert.IsTrue(ServerSocket.IsIncoming);

            static void Test(MultiStreamSocket socket)
            {
                Assert.NotNull(socket.Endpoint != null);
                Assert.AreNotEqual(socket.IdleTimeout, TimeSpan.Zero);
                Assert.Greater(socket.IncomingFrameMaxSize, 0);
                if (socket.Endpoint!.Protocol != Protocol.Ice1)
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
            ClientSocket.Abort();
            Assert.CatchAsync<TransportException>(async () => await stream.SendRequestFrameAsync(DummyRequest));
        }

        [Test]
        public async Task MultiStreamSocket_SendResponse_Failure()
        {
            var stream = ClientSocket.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);

            var serverStream = await ServerSocket.AcceptStreamAsync(default);
            var request = await serverStream.ReceiveRequestFrameAsync();
            ServerSocket.Abort();
            Assert.CatchAsync<TransportException>(
                async () => await serverStream.SendResponseFrameAsync(GetResponseFrame(request)));
        }

        [Order(1)]
        public async Task MultiStreamSocket_StreamCount()
        {
            Assert.AreEqual(0, ClientSocket.IncomingStreamCount);
            Assert.AreEqual(0, ClientSocket.OutgoingStreamCount);
            Assert.AreEqual(0, ServerSocket.IncomingStreamCount);
            Assert.AreEqual(0, ServerSocket.OutgoingStreamCount);

            var release1 = await Test(ClientSocket, ServerSocket, 1);
            var release2 = await Test(ServerSocket, ClientSocket, 1);

            var release3 = await Test(ClientSocket, ServerSocket, 2);
            var release4 = await Test(ServerSocket, ClientSocket, 2);

            release4();
            release3();

            release2();
            release1();

            async Task<Action> Test(MultiStreamSocket socket, MultiStreamSocket peerSocket, int expectedCount)
            {
                var clientStream = socket.CreateStream(true);
                Assert.AreEqual(expectedCount - 1, socket.OutgoingStreamCount);
                ValueTask task = clientStream.SendRequestFrameAsync(DummyRequest);
                Assert.AreEqual(expectedCount, socket.OutgoingStreamCount);

                Assert.AreEqual(expectedCount - 1, peerSocket.IncomingStreamCount);
                var serverStream = await peerSocket.AcceptStreamAsync(default);
                Assert.AreEqual(expectedCount, peerSocket.IncomingStreamCount);

                await task;
                return () => {
                    clientStream.Release();
                    Assert.AreEqual(expectedCount - 1, socket.OutgoingStreamCount);

                    serverStream.Release();
                    Assert.AreEqual(expectedCount - 1, peerSocket.IncomingStreamCount);
                };
            }
        }

        [SetUp]
        public Task SetUp() => SetUpSockets();

        [TearDown]
        public void TearDown() => TearDownSockets();
    }

    [Parallelizable(scope: ParallelScope.Fixtures)]
    [Timeout(10000)]
    [TestFixture(MultiStreamSocketType.Slic)]
    [TestFixture(MultiStreamSocketType.Colocated)]
    [TestFixture(MultiStreamSocketType.Ice1)]
    public class MultiStreamSocketStreamTests : MultiStreamSocketBaseTest
    {
        public MultiStreamSocketStreamTests(MultiStreamSocketType socketType)
            : base(socketType)
        {
        }

        [SetUp]
        public Task SetUp() => SetUpSockets();

        [TearDown]
        public void TearDown() => TearDownSockets();

        [TestCase(64)]
        [TestCase(1024)]
        [TestCase(32 * 1024)]
        [TestCase(128 * 1024)]
        [TestCase(512 * 1024)]
        public async Task MultiStreamSocketStream_SendReceiveRequest(int size)
        {
            var request = OutgoingRequestFrame.WithArgs(
                Proxy,
                "op",
                idempotent: false,
                compress: false,
                format: default,
                null,
                new byte[size],
                (OutputStream ostr, in ReadOnlyMemory<byte> value) =>
                {
                    ostr.WriteSequence(value.Span);
                });

            var receiveTask = PerformReceiveAsync();

            var stream = ClientSocket.CreateStream(false);
            await stream.SendRequestFrameAsync(DummyRequest);
            stream.Release();

            await receiveTask;

            async ValueTask PerformReceiveAsync()
            {
                var serverStream = await ServerSocket.AcceptStreamAsync(default);
                await serverStream.ReceiveRequestFrameAsync();
                serverStream.Release();
            }
        }

        [Test]
        public async Task MultiStreamSocketStream_SendRequest_Cancellation()
        {
            var stream = ClientSocket.CreateStream(true);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.SendRequestFrameAsync(DummyRequest, source.Token));
            stream.Release();

            if (SocketType == MultiStreamSocketType.Slic)
            {
                // With Slic, large frames are sent with multiple packets. Here we ensure that cancelling the sending
                // while the packets are being sent works.

                var request = OutgoingRequestFrame.WithArgs(
                    Proxy,
                    "op",
                    idempotent: false,
                    compress: false,
                    format: default,
                    null,
                    new byte[256 * 1024],
                    (OutputStream ostr, in ReadOnlyMemory<byte> value) =>
                    {
                        ostr.WriteSequence(value.Span);
                    });

                int requestCount = 0;
                while (true)
                {
                    stream = ClientSocket.CreateStream(true);
                    try
                    {
                        using var source2 = new CancellationTokenSource();
                        source2.CancelAfter(200);
                        ValueTask sendTask = stream.SendRequestFrameAsync(request, source2.Token);
                        await sendTask;
                        requestCount++;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    finally
                    {
                        stream.Release();
                    }
                }

                await ReceiveRequests(requestCount);
            }

            // Ensure we can still send a request after the cancellation
            stream = ClientSocket.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);
            stream.Release();

            async Task ReceiveRequests(int requestCount)
            {
                if (requestCount == 0)
                {
                    try
                    {
                        using var source = new CancellationTokenSource(500);
                        var serverStream = await ServerSocket.AcceptStreamAsync(source.Token);
                        _ = ServerSocket.AcceptStreamAsync(default).AsTask();
                        Assert.CatchAsync<TransportException>(async () => await serverStream.ReceiveRequestFrameAsync());
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
                else
                {
                    var serverStream = await ServerSocket.AcceptStreamAsync(default);
                    Task receiveNextRequestTask = ReceiveRequests(--requestCount);
                    _ = await serverStream.ReceiveRequestFrameAsync();
                    serverStream.Release();
                    await receiveNextRequestTask;
                }
            }
        }

        [Test]
        public async Task MultiStreamSocketStream_SendResponse_Cancellation()
        {
            var stream = ClientSocket.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);

            var serverStream = await ServerSocket.AcceptStreamAsync(default);
            var request = await serverStream.ReceiveRequestFrameAsync();

            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.SendResponseFrameAsync(GetResponseFrame(request), source.Token));

            stream.Release();
            serverStream.Release();
        }

        [Test]
        public void MultiStreamSocketStream_ReceiveRequest_Cancellation()
        {
            var stream = ClientSocket.CreateStream(false);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.ReceiveRequestFrameAsync(source.Token));
            stream.Release();
        }

        [Test]
        public async Task MultiStreamSocketStream_ReceiveResponse_Cancellation()
        {
            var stream = ClientSocket.CreateStream(true);
            await stream.SendRequestFrameAsync(DummyRequest);
            using var source = new CancellationTokenSource();
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await stream.ReceiveResponseFrameAsync(source.Token));
            stream.Release();
        }
    }
}
