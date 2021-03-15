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
        protected MultiStreamSocket ClientSocket => _clientSocket!;
        protected IServicePrx Proxy => _proxy!;
        protected MultiStreamSocket ServerSocket => _serverSocket!;
        protected MultiStreamSocketType SocketType { get; }
        private MultiStreamSocket? _clientSocket;
        private IServicePrx? _proxy;
        private MultiStreamSocket? _serverSocket;

        public MultiStreamSocketBaseTest(
            MultiStreamSocketType socketType,
            Action<ServerOptions>? serverOptionsBuilder = null)
            : base(socketType == MultiStreamSocketType.Ice1 ? Protocol.Ice1 : Protocol.Ice2,
                   socketType == MultiStreamSocketType.Colocated ? "colocated" : "tcp",
                   false,
                   serverOptionsBuilder) =>
            SocketType = socketType;

        [SetUp]
        public async Task SetUp()
        {
            Task<MultiStreamSocket> acceptTask = AcceptAsync();
            (_clientSocket, _proxy) = await ConnectAndGetProxyAsync();
            _serverSocket = await acceptTask;

            ValueTask initializeTask = _serverSocket.InitializeAsync(default);
            await _clientSocket.InitializeAsync(default);
            await initializeTask;
        }

        [TearDown]
        public void TearDown()
        {
            _clientSocket?.Dispose();
            _serverSocket?.Dispose();
        }
    }

    // Test the varions multi-stream socket implementations.
    [Timeout(10000)]
    [TestFixture(MultiStreamSocketType.Slic)]
    [TestFixture(MultiStreamSocketType.Colocated)]
    [TestFixture(MultiStreamSocketType.Ice1)]
    public class MultiStreamSocketTests : MultiStreamSocketBaseTest
    {
        private OutgoingRequestFrame DummyRequest => OutgoingRequestFrame.WithEmptyArgs(Proxy, "foo", false);
        private SocketStream? _controlStreamForClient;
        private SocketStream? _peerControlStreamForClient;
        private SocketStream? _controlStreamForServer;
        private SocketStream? _peerControlStreamForServer;

        public MultiStreamSocketTests(MultiStreamSocketType type)
            : base(type, serverOptions =>
                   {
                       // Setup specific server options for testing purpose
                       serverOptions.BidirectionalStreamMaxCount = 15;
                       serverOptions.UnidirectionalStreamMaxCount = 10;
                       serverOptions.IncomingFrameMaxSize = 512 * 1024;
                   })
        {
        }

        [Test]
        public void MultiStreamSocket_Abort()
        {
            ValueTask<SocketStream> acceptStreamTask = ServerSocket.AcceptStreamAsync();
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
            Assert.ThrowsAsync<ConnectionClosedException>(async () => await ServerSocket.AcceptStreamAsync());

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

            var serverStream = await ServerSocket.AcceptStreamAsync();
            var incomingRequest = await serverStream.ReceiveRequestFrameAsync(default);

            await serverStream.SendResponseFrameAsync(GetResponseFrame(incomingRequest));

            (var clientBidirectional, var clientUnidirectional) = ClientSocket.AbortStreams(ex, stream =>
            {
                Assert.AreEqual(stream, clientStream);
                return false; // Don't abort the stream
            });

            // Stream is not aborted
            var acceptTask = ClientSocket.AcceptStreamAsync();
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

            var serverStream = await ServerSocket.AcceptStreamAsync();
            var incomingRequest = await serverStream.ReceiveRequestFrameAsync(default);

            await serverStream.SendResponseFrameAsync(
                new OutgoingResponseFrame(incomingRequest, new UnhandledException(ex)),
                default);

            var acceptTask = ClientSocket.AcceptStreamAsync();
            await clientStream.ReceiveResponseFrameAsync(default);

            clientStream.Release();
            serverStream.Release();

            clientStream = ClientSocket.CreateStream(true);
            await clientStream.SendRequestFrameAsync(DummyRequest);

            serverStream = await ServerSocket.AcceptStreamAsync();
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
            ValueTask<SocketStream> acceptTask = ServerSocket.AcceptStreamAsync();

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
            var source = new CancellationTokenSource();
            ValueTask<SocketStream> acceptTask = ServerSocket.AcceptStreamAsync(source.Token);
            source.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await acceptTask);
        }

        [Test]
        public async Task MultiStreamSocket_CloseAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                // This will either complete successfully or with an OperationCanceledException depending on the
                // implementation (which might be a no-op).
                await ClientSocket.CloseAsync(new InvalidDataException(""), canceled.Token);
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
            for (int i = 0; i < Server.BidirectionalStreamMaxCount; ++i)
            {
                var stream = ClientSocket.CreateStream(true);
                clientStreams.Add(stream);

                await stream.SendRequestFrameAsync(DummyRequest);

                serverStreams.Add(await ServerSocket.AcceptStreamAsync());
                var request = await serverStreams.Last().ReceiveRequestFrameAsync();
                incomingRequest ??= request;
            }

            // Ensure the client side accepts streams to receive responses.
            ValueTask<SocketStream> acceptClientStream = ClientSocket.AcceptStreamAsync();

            var clientStream = ClientSocket.CreateStream(true);
            ValueTask sendTask = clientStream.SendRequestFrameAsync(DummyRequest);
            ValueTask<SocketStream> acceptTask = ServerSocket.AcceptStreamAsync();

            await Task.Delay(100);

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
            using var cancel = new CancellationTokenSource();

            // Start accept stream on the server side to receive transport frames.
            var acceptStreamTask = ServerSocket.AcceptStreamAsync(cancel.Token);

            await ClientSocket.PingAsync(default);
            await semaphore.WaitAsync();

            await ClientSocket.PingAsync(default);
            await ClientSocket.PingAsync(default);
            await ClientSocket.PingAsync(default);
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();
            await semaphore.WaitAsync();

            // Cancel AcceptStreamAsync
            cancel.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await acceptStreamTask);

            ServerSocket.Ping -= EventHandler;

            void EventHandler(object? state, EventArgs args) => semaphore.Release();
        }

        [Test]
        public void MultiStreamSocket_Ping_Cancellation()
        {
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await ClientSocket.PingAsync(cancel.Token));
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
                var serverStream = await peerSocket.AcceptStreamAsync();
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
        new public async Task SetUp()
        {
            await base.SetUp();

            _controlStreamForClient = await ClientSocket.SendInitializeFrameAsync(default);
            _controlStreamForServer = await ServerSocket.SendInitializeFrameAsync(default);

            _peerControlStreamForClient = await ClientSocket.ReceiveInitializeFrameAsync(default);
            _peerControlStreamForServer = await ServerSocket.ReceiveInitializeFrameAsync(default);
        }

        [TearDown]
        new public void TearDown()
        {
            _controlStreamForClient?.Release();
            _peerControlStreamForClient?.Release();
            _controlStreamForServer?.Release();
            _peerControlStreamForServer?.Release();
            base.TearDown();
        }

        static private OutgoingResponseFrame GetResponseFrame(IncomingRequestFrame request) =>
            // TODO: Fix once OutgoingRespongFrame construction is simplified to not depend on Current
            new(request, new UnhandledException(new InvalidOperationException()));

        // [Test]
        // public void MultiStreamSocket_ReceiveAsync_Cancelation()
        // {
        //     using var canceled = new CancellationTokenSource();
        //     ValueTask<int> receiveTask = ClientSocket.ReceiveAsync(new byte[1], canceled.Token);
        //     Assert.IsFalse(receiveTask.IsCompleted);
        //     canceled.Cancel();
        //     Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        // }

        // [Test]
        // public void MultiStreamSocket_ReceiveAsync_ConnectionLostException()
        // {
        //     ServerSocket.Dispose();
        //     Assert.CatchAsync<ConnectionLostException>(
        //         async () => await ClientSocket.ReceiveAsync(new byte[1], default));
        // }

        // [Test]
        // public void MultiStreamSocket_ReceiveAsync_Dispose()
        // {
        //     ClientSocket.Dispose();
        //     Assert.CatchAsync<TransportException>(async () => await ClientSocket.ReceiveAsync(new byte[1], default));
        // }

        // [Test]
        // public void MultiStreamSocket_ReceiveAsync_Exception()
        // {
        //     Assert.ThrowsAsync<ArgumentException>(
        //         async () => await ClientSocket.ReceiveAsync(Array.Empty<byte>(), default));

        //     using var canceled = new CancellationTokenSource();
        //     canceled.Cancel();
        //     Assert.CatchAsync<OperationCanceledException>(
        //         async () => await ClientSocket.ReceiveAsync(new byte[1], canceled.Token));
        // }

        // [Test]
        // public void MultiStreamSocket_SendAsync_Cancelation()
        // {
        //     ServerSocket.Socket!.ReceiveBufferSize = 4096;
        //     ClientSocket.Socket!.SendBufferSize = 4096;

        //     // On some platforms the setting of the buffer sizes might not be granted, we make sure the buffers
        //     // are at least not larger than 16KB. The test below relies on the SendAsync to block when the socket
        //     // send/receive buffers fill up.
        //     Assert.Less(ServerSocket.Socket!.ReceiveBufferSize, 16 * 1024);
        //     Assert.Less(ClientSocket.Socket!.SendBufferSize, 16 * 1024);

        //     using var canceled = new CancellationTokenSource();

        //     // Wait for the SendAsync call to block.
        //     ValueTask<int> sendTask;
        //     do
        //     {
        //         sendTask = ClientSocket.SendAsync(OneMBSendBuffer, canceled.Token);
        //     }
        //     while (sendTask.IsCompleted);
        //     sendTask = ClientSocket.SendAsync(OneMBSendBuffer, canceled.Token);
        //     Assert.IsFalse(sendTask.IsCompleted);

        //     // Cancel the blocked SendAsync and ensure OperationCanceledException is raised.
        //     canceled.Cancel();
        //     Assert.CatchAsync<OperationCanceledException>(async () => await sendTask);
        // }

        // [Test]
        // public void MultiStreamSocket_SendAsync_ConnectionLostException()
        // {
        //     ServerSocket.Dispose();
        //     Assert.CatchAsync<ConnectionLostException>(
        //         async () =>
        //         {
        //             while (true)
        //             {
        //                 await ClientSocket.SendAsync(OneMBSendBuffer, default);
        //             }
        //         });
        // }

        // [Test]
        // public void MultiStreamSocket_SendAsync_Dispose()
        // {
        //     ClientSocket.Dispose();
        //     Assert.CatchAsync<TransportException>(async () => await ClientSocket.SendAsync(OneBSendBuffer, default));
        // }

        // [Test]
        // public void MultiStreamSocket_SendAsync_Exception()
        // {
        //     using var canceled = new CancellationTokenSource();
        //     canceled.Cancel();
        //     Assert.CatchAsync<OperationCanceledException>(
        //         async () => await ClientSocket.SendAsync(OneBSendBuffer, canceled.Token));
        // }

        // [TestCase(1)]
        // [TestCase(1024)]
        // [TestCase(16 * 1024)]
        // [TestCase(512 * 1024)]
        // public async Task MultiStreamSocket_SendReceiveAsync(int size)
        // {
        //     var sendBuffer = new List<ArraySegment<byte>>() { new byte[size] };

        //     ValueTask test1 = Test(ClientSocket, ServerSocket);
        //     ValueTask test2 = Test(ServerSocket, ClientSocket);

        //     await test1;
        //     await test2;

        //     async ValueTask Test(SingleStreamSocket socket1, SingleStreamSocket socket2)
        //     {
        //         ValueTask<int> sendTask = socket1.SendAsync(sendBuffer, default);
        //         ArraySegment<byte> receiveBuffer = new byte[size];
        //         int offset = 0;
        //         while (offset < size)
        //         {
        //             offset += await socket2.ReceiveAsync(receiveBuffer.Slice(offset), default);
        //         }
        //         Assert.AreEqual(await sendTask, size);
        //     }
        // }
    }

    // [Parallelizable(scope: ParallelScope.Fixtures)]
    // [TestFixture(Protocol.Ice2, "tcp", false)]
    // [TestFixture(Protocol.Ice2, "tcp", true)]
    // [TestFixture(Protocol.Ice2, "ws", false)]
    // [TestFixture(Protocol.Ice2, "ws", true)]
    // [TestFixture(Protocol.Ice1, "tcp", false)]
    // [TestFixture(Protocol.Ice1, "ssl", true)]
    // public class AcceptSingleStreamSocketTests : SocketBaseTest
    // {
    //     public AcceptSingleStreamSocketTests(Protocol protocol, string transport, bool secure)
    //         : base(protocol, transport, secure)
    //     {
    //     }

    //     [Test]
    //     public async Task AcceptMultiStreamSocket_Acceptor_AcceptAsync()
    //     {
    //         using IAcceptor acceptor = await CreateAcceptorAsync();
    //         ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);
    //         using SingleStreamSocket clientSocket = await CreateClientSocketAsync();
    //         ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(ClientEndpoint, IsSecure, default);
    //         using SingleStreamSocket serverSocket = await acceptTask;
    //     }

    //     [Test]
    //     public async Task AcceptMultiStreamSocket_Acceptor_Constructor_TransportException()
    //     {
    //         using IAcceptor acceptor = await CreateAcceptorAsync();
    //         Assert.ThrowsAsync<TransportException>(async () => await CreateAcceptorAsync());
    //     }

    //     public async Task AcceptMultiStreamSocket_AcceptAsync()
    //     {
    //         using IAcceptor acceptor = await CreateAcceptorAsync();
    //         ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);

    //         using SingleStreamSocket clientSocket = await CreateClientSocketAsync();
    //         ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(ClientEndpoint, IsSecure, default);

    //         using SingleStreamSocket serverSocket = await acceptTask;

    //         SingleStreamSocket socket = await serverSocket.AcceptAsync(ServerEndpoint, default);
    //         await connectTask;

    //         // The SslSocket is returned if a secure connection is requested.
    //         Assert.IsTrue(IsSecure ? socket != serverSocket : socket == serverSocket);
    //     }

    //     // We eventually retry this test if it fails. The AcceptAsync can indeed not always fail if for
    //     // example the server SSL handshake completes before the RST is received.
    //     [Test]
    //     public async Task AcceptMultiStreamSocket_AcceptAsync_ConnectionLostException()
    //     {
    //         using IAcceptor acceptor = await CreateAcceptorAsync();
    //         ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);

    //         SingleStreamSocket clientSocket = await CreateClientSocketAsync();

    //         // We don't use clientSocket.ConnectAsync() here as this would start the TLS handshake for secure
    //         // connections and AcceptAsync would sometime succeed.
    //         IPAddress[] addresses = await Dns.GetHostAddressesAsync(ClientEndpoint.Host).ConfigureAwait(false);
    //         var endpoint = new IPEndPoint(addresses[0], ClientEndpoint.Port);
    //         await clientSocket.Socket!.ConnectAsync(endpoint).ConfigureAwait(false);

    //         using SingleStreamSocket serverSocket = await acceptTask;

    //         clientSocket.Dispose();

    //         AsyncTestDelegate testDelegate;
    //         if (!IsSecure && ClientEndpoint.Protocol == Protocol.Ice1 && TransportName == "tcp")
    //         {
    //             // AcceptAsync is a no-op for Ice1 non-secure TCP connections so it won't throw.
    //             await serverSocket.AcceptAsync(ServerEndpoint, default);
    //             testDelegate = async () => await serverSocket.ReceiveAsync(new byte[1], default);
    //         }
    //         else
    //         {
    //             testDelegate = async () => await serverSocket.AcceptAsync(ServerEndpoint, default);
    //         }
    //         Assert.ThrowsAsync<ConnectionLostException>(testDelegate);
    //     }

    //     [Test]
    //     public async Task AcceptMultiStreamSocket_AcceptAsync_OperationCanceledException()
    //     {
    //         using IAcceptor acceptor = await CreateAcceptorAsync();

    //         using SingleStreamSocket clientSocket = await CreateClientSocketAsync();
    //         ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(ClientEndpoint, IsSecure, default);

    //         using SingleStreamSocket serverSocket = await CreateServerSocketAsync(acceptor);

    //         using var source = new CancellationTokenSource();
    //         source.Cancel();
    //         ValueTask<SingleStreamSocket> acceptTask = serverSocket.AcceptAsync(ServerEndpoint, source.Token);

    //         if (!IsSecure && ClientEndpoint.Protocol == Protocol.Ice1 && TransportName == "tcp")
    //         {
    //             // AcceptAsync is a no-op for Ice1 non-secure TCP connections so it won't throw.
    //             await acceptTask;
    //         }
    //         else
    //         {
    //             Assert.CatchAsync<OperationCanceledException>(async () => await acceptTask);
    //         }
    //     }

    //     private async ValueTask<SingleStreamSocket> CreateClientSocketAsync()
    //     {
    //         IPAddress[] addresses = await Dns.GetHostAddressesAsync(ClientEndpoint.Host).ConfigureAwait(false);
    //         Connection connection =
    //             (ClientEndpoint as IPEndpoint)!.CreateConnection(
    //                 new IPEndPoint(addresses[0], ClientEndpoint.Port), null, default);
    //         return (connection.Socket as MultiStreamOverSingleStreamSocket)!.Underlying;
    //     }

    //     private static async ValueTask<SingleStreamSocket> CreateServerSocketAsync(IAcceptor acceptor)
    //     {
    //         MultiStreamSocket multiStreamServerSocket = (await acceptor.AcceptAsync()).Socket;
    //         return (multiStreamServerSocket as MultiStreamOverSingleStreamSocket)!.Underlying;
    //     }
    // }

    // [Parallelizable(scope: ParallelScope.Fixtures)]
    // [TestFixture(Protocol.Ice1, "tcp", false)]
    // [TestFixture(Protocol.Ice1, "ssl", true)]
    // [TestFixture(Protocol.Ice2, "tcp", false)]
    // [TestFixture(Protocol.Ice2, "tcp", true)]
    // [TestFixture(Protocol.Ice2, "ws", false)]
    // [TestFixture(Protocol.Ice2, "ws", true)]
    // [Timeout(5000)]
    // public class ConnectSingleStreamSocketTests : SocketBaseTest
    // {
    //     public ConnectSingleStreamSocketTests(Protocol protocol, string transport, bool secure)
    //         : base(protocol, transport, secure)
    //     {
    //     }

    //     [Test]
    //     public async Task ConnectMultiStreamSocket_ConnectAsync_ConnectionRefusedException()
    //     {
    //         using SingleStreamSocket clientSocket = await CreateClientSocketAsync();
    //         Assert.ThrowsAsync<ConnectionRefusedException>(
    //             async () => await clientSocket.ConnectAsync(ClientEndpoint, IsSecure, default));
    //     }

    //     [Test]
    //     public async Task ConnectMultiStreamSocket_ConnectAsync_OperationCanceledException()
    //     {
    //         using IAcceptor acceptor = await CreateAcceptorAsync();

    //         using var source = new CancellationTokenSource();
    //         if (!IsSecure && TransportName == "tcp")
    //         {
    //             // ConnectAsync might complete synchronously with TCP
    //         }
    //         else
    //         {
    //             using SingleStreamSocket clientSocket = await CreateClientSocketAsync();
    //             ValueTask<SingleStreamSocket> connectTask =
    //                 clientSocket.ConnectAsync(ClientEndpoint, IsSecure, source.Token);
    //             source.Cancel();
    //             Assert.CatchAsync<OperationCanceledException>(async () => await connectTask);
    //         }

    //         using var source2 = new CancellationTokenSource();
    //         source2.Cancel();
    //         using SingleStreamSocket clientSocket2 = await CreateClientSocketAsync();
    //         Assert.CatchAsync<OperationCanceledException>(
    //             async () => await clientSocket2.ConnectAsync(ClientEndpoint, IsSecure, source2.Token));
    //     }

    //     private async ValueTask<SingleStreamSocket> CreateClientSocketAsync()
    //     {
    //         IPAddress[] addresses = await Dns.GetHostAddressesAsync(ClientEndpoint.Host).ConfigureAwait(false);
    //         Connection connection =
    //             (ClientEndpoint as IPEndpoint)!.CreateConnection(
    //                 new IPEndPoint(addresses[0], ClientEndpoint.Port), null, default);
    //         return (connection.Socket as MultiStreamOverSingleStreamSocket)!.Underlying;
    //     }
    // }
}
