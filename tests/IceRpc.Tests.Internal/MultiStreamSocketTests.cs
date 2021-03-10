// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.IO;
using System.Net.Sockets;
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
        public MultiStreamSocketTests(MultiStreamSocketType type)
            : base(type, serverOptions =>
                   {
                       // Setup specific server options for testing purpose
                       serverOptions.BidirectionalStreamMaxCount = 50;
                       serverOptions.UnidirectionalStreamMaxCount = 40;
                       serverOptions.IncomingFrameMaxSize = 512 * 1024;
                   })
        {
        }

        [Test]
        public void MultiStreamSocket_Abort()
        {
            ValueTask<SocketStream> acceptStreamTask = ServerSocket.AcceptStreamAsync(default);
            ClientSocket.Abort();
            Assert.ThrowsAsync<ConnectionLostException>(async () => await acceptStreamTask);
        }

        [Test]
        public async Task MultiStreamSocket_AcceptStream()
        {
            SocketStream clientStream = ClientSocket.CreateStream(bidirectional: true);
            ValueTask<SocketStream> acceptTask = ServerSocket.AcceptStreamAsync(default);

            // The server-side won't accept the stream until the first frame is sent.
            await clientStream.SendRequestFrameAsync(OutgoingRequestFrame.WithEmptyArgs(Proxy, "foo", false), default);

            SocketStream serverStream = await acceptTask;

            Assert.IsTrue(serverStream.IsBidirectional);
            Assert.IsTrue(serverStream.IsStarted);
            Assert.IsFalse(serverStream.IsControl);
            Assert.AreEqual(serverStream.Id, clientStream.Id);

            clientStream.Release();
            serverStream.Release();
        }

        [Test]
        public async Task MultiStreamSocket_CloseAsync_Exception()
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

        [Test]
        public void MultiStreamSocket_CreateStream()
        {
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
        public void MultiStreamSocket_Initialize()
        {
        }

        [Test]
        public async Task MultiStreamSocket_PeerIncomingFrameMaxSize()
        {
            // Exchange Initialize frame to receive the peer incoming frame max size
            Assert.IsNull(ServerSocket.PeerIncomingFrameMaxSize);
            ValueTask<SocketStream> receiveTask = ServerSocket.ReceiveInitializeFrameAsync(default);
            _ = await ClientSocket.SendInitializeFrameAsync(default);
            _ = await receiveTask;

            Assert.IsNull(ClientSocket.PeerIncomingFrameMaxSize);
            ValueTask<SocketStream> receiveTask2 = ClientSocket.ReceiveInitializeFrameAsync(default);
            await ServerSocket.SendInitializeFrameAsync(default);
            await receiveTask2;

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
        public void MultiStreamSocket_Ping()
        {
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
                Assert.IsNull(socket.PeerIncomingFrameMaxSize);
                Assert.IsNotEmpty(socket.ToString());
                Assert.AreNotEqual(socket.LastActivity, TimeSpan.Zero);
                Assert.AreEqual(socket.LastResponseStreamId, 0);
                Assert.AreEqual(socket.IncomingStreamCount, 0);
                Assert.AreEqual(socket.OutgoingStreamCount, 0);
            }

            Assert.AreEqual(ServerSocket.IncomingFrameMaxSize, 512 * 1024);
            Assert.AreEqual(ClientSocket.IncomingFrameMaxSize, 1024 * 1024);
        }

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
