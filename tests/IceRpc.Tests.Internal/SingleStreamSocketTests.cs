// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    public class SingleStreamSocketBaseTest : SocketBaseTest
    {
        protected static readonly List<ArraySegment<byte>> OneBSendBuffer = new() { new byte[1] };
        protected static readonly List<ArraySegment<byte>> OneMBSendBuffer = new() { new byte[1024 * 1024] };
        protected SingleStreamSocket ClientSocket => _clientSocket!;
        protected SingleStreamSocket ServerSocket => _serverSocket!;
        private SingleStreamSocket? _clientSocket;
        private SingleStreamSocket? _serverSocket;

        public SingleStreamSocketBaseTest(
            Protocol protocol,
            string transport,
            bool secure,
            bool ipv6,
            Action<ConnectionOptions>? clientConnectionOptionsBuilder = null,
            Action<ConnectionOptions>? serverConnectionOptionsBuilder = null)
            : base(
                protocol,
                transport,
                secure,
                ipv6,
                clientConnectionOptionsBuilder: clientConnectionOptionsBuilder,
                serverConnectionOptionsBuilder: serverConnectionOptionsBuilder)
        {
        }

        [SetUp]
        public async Task SetUp()
        {
            if (ClientEndpoint.IsDatagram)
            {
                _serverSocket = ((MultiStreamOverSingleStreamSocket)CreateDatagramServerSocket()).Underlying;
                ValueTask<SingleStreamSocket> connectTask = SingleStreamSocket(ConnectAsync());
                _clientSocket = await connectTask;
            }
            else
            {
                ValueTask<SingleStreamSocket> connectTask = SingleStreamSocket(ConnectAsync());
                ValueTask<SingleStreamSocket> acceptTask = SingleStreamSocket(AcceptAsync());

                _clientSocket = await connectTask;
                _serverSocket = await acceptTask;
            }

            static async ValueTask<SingleStreamSocket> SingleStreamSocket(Task<MultiStreamSocket> socket) =>
                (await socket as MultiStreamOverSingleStreamSocket)!.Underlying;
        }

        [TearDown]
        public void TearDown()
        {
            _clientSocket?.Dispose();
            _serverSocket?.Dispose();
        }
    }

    // Test the varions single socket implementations. We don't test Ice1 + WS here as it doesn't really
    // provide additional test coverage given that the WS socket has no protocol specific code.
    [TestFixture("tcp", false)]
    [TestFixture("ws", false)]
    [TestFixture("tcp", true)] // secure
    [TestFixture("ws", true)] // secure
    [TestFixture("udp", false)]
    [Timeout(5000)]
    public class SingleStreamSocketTests : SingleStreamSocketBaseTest
    {
        public SingleStreamSocketTests(string transport, bool secure)
            : base(transport == "udp" ? Protocol.Ice1 : Protocol.Ice2, transport, secure, ipv6: false)
        {
        }

        [Test]
        public async Task SingleStreamSocket_CloseAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                // This will either complete successfully or with an OperationCanceledException
                await ClientSocket.CloseAsync(new InvalidDataException(""), canceled.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        [Test]
        public void SingleStreamSocket_Dispose()
        {
            ClientSocket.Dispose();
            ServerSocket.Dispose();
            ClientSocket.Dispose();
            ServerSocket.Dispose();
        }

        [Test]
        public void SingleStreamSocket_Properties()
        {
            Test(ClientSocket);
            Test(ServerSocket);

            void Test(SingleStreamSocket socket)
            {
                Assert.NotNull(socket.Socket);
                Assert.AreEqual(socket.SslStream != null, IsSecure);
                Assert.IsNotEmpty(socket.ToString());
            }
        }

        [Test]
        public void SingleStreamSocket_SendAsync_Dispose()
        {
            ClientSocket.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.SendAsync(OneBSendBuffer, default));
        }

        [Test]
        public void SingleStreamSocket_SendAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientSocket.SendAsync(OneBSendBuffer, canceled.Token));
        }
    }

    [TestFixture("tcp", false, false)]
    [TestFixture("ws", false, false)]
    [TestFixture("tcp", true, false)] // secure
    [TestFixture("ws", true, false)] // secure
    [TestFixture("tcp", false, true)] // ipv6
    [Timeout(5000)]
    public class NonDatagramTests : SingleStreamSocketBaseTest
    {
        public NonDatagramTests(string transport, bool secure, bool ipv6)
            : base(Protocol.Ice2, transport, secure, ipv6)
        {
        }

        [Test]
        public void NonDatagramSocket_ReceiveAsync_Cancelation()
        {
            using var canceled = new CancellationTokenSource();
            ValueTask<int> receiveTask = ClientSocket.ReceiveAsync(new byte[1], canceled.Token);
            Assert.IsFalse(receiveTask.IsCompleted);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        }

        [Test]
        public void NonDatagramSocket_ReceiveAsync_ConnectionLostException()
        {
            ServerSocket.Dispose();
            Assert.CatchAsync<ConnectionLostException>(
                async () => await ClientSocket.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void NonDatagramSocket_ReceiveAsync_Dispose()
        {
            ClientSocket.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void NonDatagramSocket_ReceiveAsync_Exception()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await ClientSocket.ReceiveAsync(Array.Empty<byte>(), default));

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientSocket.ReceiveAsync(new byte[1], canceled.Token));
        }

        [Test]
        public void NonDatagramSocket_ReceiveDatagramAsync_Exception()
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await ClientSocket.ReceiveDatagramAsync(default));
        }

        [Test]
        public async Task NonDatagramSocket_SendAsync_Cancelation()
        {
            ServerSocket.Socket!.ReceiveBufferSize = 4096;
            ClientSocket.Socket!.SendBufferSize = 4096;

            // On some platforms the setting of the buffer sizes might not be granted, we make sure the buffers
            // are at least not larger than 16KB. The test below relies on the SendAsync to block when the socket
            // send/receive buffers fill up.
            Assert.Less(ServerSocket.Socket!.ReceiveBufferSize, 16 * 1024);
            Assert.Less(ClientSocket.Socket!.SendBufferSize, 16 * 1024);

            using var canceled = new CancellationTokenSource();

            // Wait for the SendAsync call to block.
            Task<int> sendTask;
            do
            {
                sendTask = ClientSocket.SendAsync(OneMBSendBuffer, canceled.Token).AsTask();
                await Task.WhenAny(Task.Delay(500), sendTask);
            }
            while (sendTask.IsCompleted);
            sendTask = ClientSocket.SendAsync(OneMBSendBuffer, canceled.Token).AsTask();
            Assert.IsFalse(sendTask.IsCompleted);

            // Cancel the blocked SendAsync and ensure OperationCanceledException is raised.
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await sendTask);
        }

        [Test]
        public void NonDatagramSocket_SendAsync_ConnectionLostException()
        {
            ServerSocket.Dispose();
            Assert.CatchAsync<ConnectionLostException>(
                async () =>
                {
                    while (true)
                    {
                        await ClientSocket.SendAsync(OneMBSendBuffer, default);
                    }
                });
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(16 * 1024)]
        [TestCase(512 * 1024)]
        public async Task NonDatagramSocket_SendReceiveAsync(int size)
        {
            var sendBuffer = new List<ArraySegment<byte>>() { new byte[size] };

            ValueTask test1 = Test(ClientSocket, ServerSocket);
            ValueTask test2 = Test(ServerSocket, ClientSocket);

            await test1;
            await test2;

            async ValueTask Test(SingleStreamSocket socket1, SingleStreamSocket socket2)
            {
                ValueTask<int> sendTask = socket1.SendAsync(sendBuffer, default);
                ArraySegment<byte> receiveBuffer = new byte[size];
                int offset = 0;
                while (offset < size)
                {
                    offset += await socket2.ReceiveAsync(receiveBuffer.Slice(offset), default);
                }
                Assert.AreEqual(await sendTask, size);
            }
        }
    }

    // [TestFixture(false)]
    [TestFixture(true)]
    [Timeout(5000)]
    public class DatagramTests : SingleStreamSocketBaseTest
    {
        public DatagramTests(bool ipv6)
            : base(
                Protocol.Ice1,
                "udp",
                secure: false,
                ipv6,
                serverConnectionOptionsBuilder: options => (options.SocketOptions ??= new()).IsIPv6Only = true)
        {
        }

        [Test]
        public void DatagramSocket_ReceiveDatagramAsync_Cancelation()
        {
            using var canceled = new CancellationTokenSource();
            ValueTask<ArraySegment<byte>> receiveTask = ClientSocket.ReceiveDatagramAsync(canceled.Token);
            Assert.IsFalse(receiveTask.IsCompleted);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await receiveTask);
        }

        [Test]
        public void DatagramSocket_ReceiveDatagramAsync_Dispose()
        {
            ClientSocket.Dispose();
            Assert.CatchAsync<TransportException>(async () => await ClientSocket.ReceiveDatagramAsync(default));
        }

        [Test]
        public void DatagramSocket_ReceiveAsync_Exception()
        {
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await ClientSocket.ReceiveAsync(new byte[1], default));
        }

        [Test]
        public void DatagramSocket_SendAsync_Cancelation()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var buffer = new List<ArraySegment<byte>>() { new byte[1] };
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientSocket.SendAsync(buffer, canceled.Token));
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(4096)]
        public async Task DatagramSocket_SendReceiveAsync(int size)
        {
            var sendBuffer = new List<ArraySegment<byte>>() { new byte[size] };
            new Random().NextBytes(sendBuffer[0]);

            // Datagrams aren't reliable, try up to 5 times in case the datagram is lost.
            int count = 5;
            while(count-- > 0)
            {
                try
                {
                    using var source = new CancellationTokenSource(1000);
                    ValueTask<int> sendTask = ClientSocket.SendAsync(sendBuffer, default);
                    ArraySegment<byte> receiveBuffer = await ServerSocket.ReceiveDatagramAsync(source.Token);
                    Assert.AreEqual(await sendTask, receiveBuffer.Count);
                    Assert.AreEqual(sendBuffer[0], receiveBuffer);
                    break;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(4096)]
        public async Task DatagramSocket_SendReceiveBidirAsync(int size)
        {
            var sendBuffer = new List<ArraySegment<byte>>() { new byte[size] };
            new Random().NextBytes(sendBuffer[0]);

            // Datagrams aren't reliable, try up to 5 times in case the datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    // Send a datagram client to server socket.
                    using var source = new CancellationTokenSource(1000);
                    ValueTask<int> sendTask = ClientSocket.SendAsync(sendBuffer, default);
                    ArraySegment<byte> receiveBuffer = await ServerSocket.ReceiveDatagramAsync(source.Token);
                    Assert.AreEqual(await sendTask, receiveBuffer.Count);
                    Assert.AreEqual(sendBuffer[0], receiveBuffer);

                    // Send back a datagram from server socket to client.
                    ValueTask<int> sendTask2 = ServerSocket.SendAsync(sendBuffer, default);
                    receiveBuffer = await ClientSocket.ReceiveDatagramAsync(source.Token);
                    Assert.AreEqual(await sendTask2, receiveBuffer.Count);
                    Assert.AreEqual(sendBuffer[0], receiveBuffer);
                    break;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    [TestFixture(1, "239.255.1.1")]
    [TestFixture(1, "\"ff15::1:1\"")]
    [TestFixture(5, "239.255.1.1")]
    [TestFixture(5, "\"ff15::1:1\"")]
    [Timeout(5000)]
    public class DatagramMulticastTests : SocketBaseTest
    {
        protected SingleStreamSocket ClientSocket => _clientSocket!;
        protected IList<SingleStreamSocket> ServerSockets => _serverSockets;
        private SingleStreamSocket? _clientSocket;
        private readonly int _incomingConnectionCount;
        private readonly List<SingleStreamSocket> _serverSockets = new();

        public DatagramMulticastTests(int incomingConnectionCount, string multicastAddress)
            : base(
                Protocol.Ice1,
                "udp",
                secure: false,
                ipv6: multicastAddress.Contains(":"),
                clientEndpoint: (host, port) => $"udp -h {multicastAddress} -p {port}" +
                    (OperatingSystem.IsLinux() ? "" : $" --interface {host}"),
                serverEndpoint: (host, port) => $"udp -h {multicastAddress} -p {port}")
                => _incomingConnectionCount = incomingConnectionCount;

        [SetUp]
        public async Task SetUp()
        {
            _serverSockets.Clear();
            for(int i = 0; i < _incomingConnectionCount; ++i)
            {
                _serverSockets.Add(((MultiStreamOverSingleStreamSocket)CreateDatagramServerSocket()).Underlying);

            }

            ValueTask<SingleStreamSocket> connectTask = SingleStreamSocket(ConnectAsync());
            _clientSocket = await connectTask;

            static async ValueTask<SingleStreamSocket> SingleStreamSocket(Task<MultiStreamSocket> socket) =>
                (await socket as MultiStreamOverSingleStreamSocket)!.Underlying;
        }

        [TearDown]
        public void TearDown()
        {
            _clientSocket?.Dispose();
            _serverSockets.ForEach(socket => socket.Dispose());
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(4096)]
        public async Task DatagramMulticastSocket_SendReceiveAsync(int size)
        {
            var sendBuffer = new List<ArraySegment<byte>>() { new byte[size] };
            new Random().NextBytes(sendBuffer[0]);

            // Datagrams aren't reliable, try up to 5 times in case a datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    using var source = new CancellationTokenSource(1000);
                    ValueTask<int> sendTask = ClientSocket.SendAsync(sendBuffer, default);
                    foreach (SingleStreamSocket socket in ServerSockets)
                    {
                        ArraySegment<byte> receiveBuffer = await socket.ReceiveDatagramAsync(source.Token);
                        Assert.AreEqual(await sendTask, receiveBuffer.Count);
                        Assert.AreEqual(sendBuffer[0], receiveBuffer);
                    }
                    break;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    // Test graceful close WS implementation. CloseAsync methods are no-ops for TCP/SSL and complete immediately
    // rather than waiting for the peer close notification so we can't test them like we do for WS.
    [TestFixture("ws", false)]
    [TestFixture("ws", true)] // secure
    public class WSSocketTests : SingleStreamSocketBaseTest
    {
        public WSSocketTests(string transport, bool secure)
            : base(Protocol.Ice2, transport, secure, ipv6: false)
        {
        }

        [Test]
        public async Task WSSocket_CloseAsync()
        {
            ValueTask<int> serverReceiveTask = ServerSocket.ReceiveAsync(new byte[1], default);
            ValueTask<int> clientReceiveTask = ClientSocket.ReceiveAsync(new byte[1], default);

            await ClientSocket.CloseAsync(new InvalidDataException(""), default);

            // Wait for the server to send back a close frame.
            Assert.ThrowsAsync<ConnectionLostException>(async () => await clientReceiveTask);

            // Close the socket to unblock the server socket.
            ClientSocket.Dispose();

            Assert.ThrowsAsync<ConnectionLostException>(async () => await serverReceiveTask);
        }
    }

    // Testing the Ice1 and Ice2 protocol here is useful because of the handling of secure vs non-secure
    // incoming connection which is different (with Ice2, the acceptor peeks a byte on the socket to
    // figure out if the outgoing connection is a secure or non-secure connection).
    [TestFixture(Protocol.Ice2, "tcp", false)]
    [TestFixture(Protocol.Ice2, "tcp", true)]
    [TestFixture(Protocol.Ice2, "ws", false)]
    [TestFixture(Protocol.Ice2, "ws", true)]
    [TestFixture(Protocol.Ice1, "tcp", false)]
    [TestFixture(Protocol.Ice1, "ssl", true)]
    [Timeout(5000)]
    public class AcceptSingleStreamSocketTests : SocketBaseTest
    {
        public AcceptSingleStreamSocketTests(Protocol protocol, string transport, bool secure)
            : base(protocol, transport, secure)
        {
        }

        [Test]
        public async Task AcceptSingleStreamSocket_Acceptor_AcceptAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();
            ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);

            using SingleStreamSocket clientSocket = CreateClientSocket();
            ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);
            using SingleStreamSocket serverSocket = await acceptTask;
        }

        [Test]
        public void AcceptSingleStreamSocket_Acceptor_Constructor_TransportException()
        {
            using IAcceptor acceptor = CreateAcceptor();
            Assert.Throws<TransportException>(() => CreateAcceptor());
        }

        public async Task AcceptSingleStreamSocket_AcceptAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();
            ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);

            using SingleStreamSocket clientSocket = CreateClientSocket();
            ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);

            using SingleStreamSocket serverSocket = await acceptTask;

            SingleStreamSocket socket = await serverSocket.AcceptAsync(
                ServerEndpoint,
                ServerAuthenticationOptions,
                default);
            await connectTask;

            // The SslSocket is returned if a secure connection is requested.
            Assert.IsTrue(IsSecure ? socket != serverSocket : socket == serverSocket);
        }

        // We eventually retry this test if it fails. The AcceptAsync can indeed not always fail if for
        // example the server SSL handshake completes before the RST is received.
        [Test]
        public async Task AcceptSingleStreamSocket_AcceptAsync_ConnectionLostException()
        {
            using IAcceptor acceptor = CreateAcceptor();
            ValueTask<SingleStreamSocket> acceptTask = CreateServerSocketAsync(acceptor);

            SingleStreamSocket clientSocket = CreateClientSocket();

            // We don't use clientSocket.ConnectAsync() here as this would start the TLS handshake for secure
            // connections and AcceptAsync would sometime succeed.
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(ClientEndpoint.Host).ConfigureAwait(false);
            var endpoint = new IPEndPoint(addresses[0], ClientEndpoint.Port);
            await clientSocket.Socket!.ConnectAsync(endpoint).ConfigureAwait(false);

            using SingleStreamSocket serverSocket = await acceptTask;

            clientSocket.Dispose();

            AsyncTestDelegate testDelegate;
            if (!IsSecure && ClientEndpoint.Protocol == Protocol.Ice1 && TransportName == "tcp")
            {
                // AcceptAsync is a no-op for Ice1 non-secure TCP connections so it won't throw.
                await serverSocket.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    default);
                testDelegate = async () => await serverSocket.ReceiveAsync(new byte[1], default);
            }
            else
            {
                testDelegate = async () => await serverSocket.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    default);
            }
            Assert.ThrowsAsync<ConnectionLostException>(testDelegate);
        }

        [Test]
        public async Task AcceptSingleStreamSocket_AcceptAsync_OperationCanceledException()
        {
            using IAcceptor acceptor = CreateAcceptor();

            using SingleStreamSocket clientSocket = CreateClientSocket();
            ValueTask<SingleStreamSocket> connectTask = clientSocket.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);

            using SingleStreamSocket serverSocket = await CreateServerSocketAsync(acceptor);

            using var source = new CancellationTokenSource();
            source.Cancel();
            ValueTask<SingleStreamSocket> acceptTask = serverSocket.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    source.Token);

            if (!IsSecure && ClientEndpoint.Protocol == Protocol.Ice1 && TransportName == "tcp")
            {
                // AcceptAsync is a no-op for Ice1 non-secure TCP connections so it won't throw.
                await acceptTask;
            }
            else
            {
                Assert.CatchAsync<OperationCanceledException>(async () => await acceptTask);
            }
        }

        private SingleStreamSocket CreateClientSocket()
        {
            TcpEndpoint endpoint = (TcpEndpoint)ClientEndpoint;
            OutgoingConnectionOptions options = ClientConnectionOptions;
            EndPoint addr = new IPEndPoint(endpoint.Address, endpoint.Port);
            SingleStreamSocket socket = endpoint.CreateSocket(addr, options.SocketOptions!, Logger);
            MultiStreamOverSingleStreamSocket multiStreamSocket = ClientEndpoint.Protocol switch
            {
                Protocol.Ice1 => new Ice1NetworkSocket(ClientEndpoint, socket, options, Logger),
                _ => new SlicSocket(ClientEndpoint, socket, options, Logger)
            };
            Connection connection = endpoint.CreateConnection(multiStreamSocket, options, server: null);
            return (connection.Socket as MultiStreamOverSingleStreamSocket)!.Underlying;
        }

        private static async ValueTask<SingleStreamSocket> CreateServerSocketAsync(IAcceptor acceptor)
        {
            MultiStreamSocket multiStreamServerSocket = (await acceptor.AcceptAsync()).Socket;
            return (multiStreamServerSocket as MultiStreamOverSingleStreamSocket)!.Underlying;
        }
    }

    // Testing the Ice1 and Ice2 protocol here is useful because of the handling of secure vs non-secure
    // incoming connection which is different (with Ice2, the acceptor peeks a byte on the socket to
    // figure out if the outgoing connection is a secure or non-secure connection).
    [TestFixture(Protocol.Ice1, "tcp", false)]
    [TestFixture(Protocol.Ice1, "ssl", true)]
    [TestFixture(Protocol.Ice2, "tcp", false)]
    [TestFixture(Protocol.Ice2, "tcp", true)]
    [TestFixture(Protocol.Ice2, "ws", false)]
    [TestFixture(Protocol.Ice2, "ws", true)]
    [Timeout(5000)]
    public class ConnectSingleStreamSocketTests : SocketBaseTest
    {
        public ConnectSingleStreamSocketTests(Protocol protocol, string transport, bool secure)
            : base(protocol, transport, secure)
        {
        }

        [Test]
        public void ConnectSingleStreamSocket_ConnectAsync_ConnectionRefusedException()
        {
            using SingleStreamSocket clientSocket = CreateClientSocket();
            Assert.ThrowsAsync<ConnectionRefusedException>(
                async () => await clientSocket.ConnectAsync(
                    ClientEndpoint,
                    ClientAuthenticationOptions,
                    default));
        }

        [Test]
        public void ConnectSingleStreamSocket_ConnectAsync_OperationCanceledException()
        {
            using IAcceptor acceptor = CreateAcceptor();

            using var source = new CancellationTokenSource();
            if (!IsSecure && TransportName == "tcp")
            {
                // ConnectAsync might complete synchronously with TCP
            }
            else
            {
                using SingleStreamSocket clientSocket = CreateClientSocket();
                ValueTask<SingleStreamSocket> connectTask =
                    clientSocket.ConnectAsync(
                        ClientEndpoint,
                        ClientAuthenticationOptions,
                        source.Token);
                source.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await connectTask);
            }

            using var source2 = new CancellationTokenSource();
            source2.Cancel();
            using SingleStreamSocket clientSocket2 = CreateClientSocket();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await clientSocket2.ConnectAsync(
                    ClientEndpoint,
                    ClientAuthenticationOptions,
                    source2.Token));
        }

        private SingleStreamSocket CreateClientSocket()
        {
            TcpEndpoint endpoint = (TcpEndpoint)ClientEndpoint;
            OutgoingConnectionOptions options = ClientConnectionOptions;
            EndPoint addr = new IPEndPoint(endpoint.Address, endpoint.Port);
            SingleStreamSocket socket = endpoint.CreateSocket(addr, options.SocketOptions!, Logger);
            MultiStreamOverSingleStreamSocket multiStreamSocket = ClientEndpoint.Protocol switch
            {
                Protocol.Ice1 => new Ice1NetworkSocket(ClientEndpoint, socket, options, Logger),
                _ => new SlicSocket(ClientEndpoint, socket, options, Logger)
            };
            Connection connection = endpoint.CreateConnection(multiStreamSocket, options, server: null);
            return (connection.Socket as MultiStreamOverSingleStreamSocket)!.Underlying;
        }
    }
}
