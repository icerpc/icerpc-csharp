// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    [TestFixture(false, AddressFamily.InterNetwork)]
    [TestFixture(true, AddressFamily.InterNetwork)]
    [TestFixture(false, AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class TcpSimpleStreamTests
    {
        private static readonly ReadOnlyMemory<ReadOnlyMemory<byte>> _oneBSendBuffer =
            new ReadOnlyMemory<byte>[] { new byte[1] };
        private static readonly ReadOnlyMemory<ReadOnlyMemory<byte>> _oneMBSendBuffer =
            new ReadOnlyMemory<byte>[] { new byte[1024 * 1024] };

        private ISimpleStream ClientStream => _clientStream!;
        private ISimpleStream ServerStream => _serverStream!;

        private readonly SslClientAuthenticationOptions _clientAuthenticationOptions;
        private ISimpleNetworkConnection? _clientConnection;
        private ISimpleStream? _clientStream;
        private readonly IListener<ISimpleNetworkConnection> _listener;

        private readonly bool _isIPv6;
        private ISimpleStream? _serverStream;

        private readonly SslServerAuthenticationOptions _serverAuthenticationOptions;
        private ISimpleNetworkConnection? _serverConnection;

        private readonly bool _tls;

        public TcpSimpleStreamTests(bool tls, AddressFamily addressFamily)
        {
            _isIPv6 = addressFamily == AddressFamily.InterNetworkV6;
            _tls = tls;

            _clientAuthenticationOptions = new()
            {
                RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection()
                                {
                                    new X509Certificate2("../../../certs/cacert.pem")
                                }),
                TargetHost = _isIPv6 ? "[::1]" : "127.0.0.1"
            };

            _serverAuthenticationOptions = new()
            {
                ClientCertificateRequired = false,
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
            };

            IServerTransport<ISimpleNetworkConnection> serverTransport =
                new TcpServerTransport(new TcpOptions(), _serverAuthenticationOptions);

            string host = _isIPv6 ? "[::1]" : "127.0.0.1";

            _listener = serverTransport.Listen($"ice+tcp://{host}:0?tls={_tls}",
                                               LogAttributeLoggerFactory.Instance);
        }

        [SetUp]
        public async Task SetupAsync()
        {
            Task<ISimpleNetworkConnection> acceptTask = _listener.AcceptAsync().AsTask();

            IClientTransport<ISimpleNetworkConnection> clientTransport =
                new TcpClientTransport(new TcpOptions(), _clientAuthenticationOptions);

            _clientConnection =
                clientTransport.CreateConnection(_listener.Endpoint, LogAttributeLoggerFactory.Instance);
            Task<(ISimpleStream, NetworkConnectionInformation)> connectTask = _clientConnection.ConnectAsync(default);

            _serverConnection = await acceptTask;
            Task<(ISimpleStream, NetworkConnectionInformation)> serverConnectTask =
                _serverConnection.ConnectAsync(default);

            _serverStream = (await serverConnectTask).Item1;
            _clientStream = (await connectTask).Item1;
        }

        [TearDown]
        public void TearDown()
        {
            _clientConnection?.Close();
            _serverConnection?.Close();
        }

        [OneTimeTearDown]
        public void Shutdown() => _listener.Dispose();

        [Test]
        public void TcpSimpleStream_ReadAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            ValueTask<int> readTask = ClientStream.ReadAsync(new byte[1], canceled.Token);
            Assert.That(readTask.IsCompleted, Is.False);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await readTask);
        }

        [Test]
        public void TcpSimpleStream_ReadAsync_ConnectionLostException()
        {
            _serverConnection!.Close();
            Assert.CatchAsync<ConnectionLostException>(
                async () => await ClientStream.ReadAsync(new byte[1], default));
        }

        [Test]
        public void TcpSimpleStream_ReadAsync_Dispose()
        {
            _clientConnection!.Close();
            Assert.CatchAsync<TransportException>(async () => await ClientStream.ReadAsync(new byte[1], default));
        }

        [Test]
        public void TcpSimpleStream_ReadAsync_Exception()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await ClientStream.ReadAsync(Array.Empty<byte>(), default));

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientStream.ReadAsync(new byte[1], canceled.Token));
        }

        [Test]
        public async Task TcpSimpleStream_WriteAsync_CancellationAsync()
        {
            Socket serverSocket = ((TcpNetworkConnection?)_serverConnection)!.Socket;
            Socket clientSocket = ((TcpNetworkConnection?)_clientConnection)!.Socket;
            serverSocket.ReceiveBufferSize = 4096;
            clientSocket.SendBufferSize = 4096;

            // On some platforms the setting of the buffer sizes might not be granted, we make sure the buffers
            // are at least not larger than 16KB. The test below relies on the SendAsync to block when the connection
            // send/receive buffers fill up.
            Assert.That(serverSocket.ReceiveBufferSize, Is.LessThan(16 * 1024));
            Assert.That(clientSocket.SendBufferSize, Is.LessThan(16 * 1024));

            using var canceled = new CancellationTokenSource();

            // Wait for the WriteAsync call to block.
            Task writeTask;
            do
            {
                writeTask = ClientStream.WriteAsync(_oneMBSendBuffer, canceled.Token).AsTask();
                await Task.WhenAny(Task.Delay(500), writeTask);
            }
            while (writeTask.IsCompleted);

            // Cancel the blocked SendAsync and ensure OperationCanceledException is raised.
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await writeTask);
        }

        [Test]
        public void TcpSimpleStream_WriteAsync_ConnectionLostException()
        {
            _serverConnection!.Close();
            Assert.CatchAsync<ConnectionLostException>(
                async () =>
                {
                    while (true)
                    {
                        await ClientStream.WriteAsync(_oneMBSendBuffer, default);
                    }
                });
        }

        [Test]
        public void TcpSimpleStream_WriteAsync_Close()
        {
            _clientConnection!.Close();
            Assert.CatchAsync<TransportException>(async () => await ClientStream.WriteAsync(_oneBSendBuffer, default));
        }

        [Test]
        public void TcpSimpleStream_WriteAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientStream.WriteAsync(_oneBSendBuffer, canceled.Token));
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(16 * 1024)]
        [TestCase(512 * 1024)]
        public async Task TcpSimpleStream_ReadWriteAsync(int size)
        {
            byte[] writeBuffer = new byte[size];

            ValueTask test1 = Test(ClientStream, ServerStream);
            ValueTask test2 = Test(ServerStream, ClientStream);

            await test1;
            await test2;

            async ValueTask Test(ISimpleStream stream1, ISimpleStream stream2)
            {
                ValueTask writeTask = stream1.WriteAsync(new ReadOnlyMemory<byte>[] { writeBuffer }, default);
                Memory<byte> readBuffer = new byte[size];
                int offset = 0;
                while (offset < size)
                {
                    offset += await stream2.ReadAsync(readBuffer[offset..], default);
                }
            }
        }
    }
}
