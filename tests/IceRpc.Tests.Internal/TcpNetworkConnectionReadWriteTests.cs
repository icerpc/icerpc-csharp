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
    public class TcpNetworkConnectionReadWriteTests
    {
        private static readonly IReadOnlyList<ReadOnlyMemory<byte>> _oneBWriteBuffer =
            new List<ReadOnlyMemory<byte>>() { new byte[1] };
        private static readonly IReadOnlyList<ReadOnlyMemory<byte>> _oneMBWriteBuffer =
            new List<ReadOnlyMemory<byte>>() { new byte[1024 * 1024] };

        private ISimpleNetworkConnection ClientConnection => _clientConnection!;
        private ISimpleNetworkConnection ServerConnection => _serverConnection!;

        private ISimpleNetworkConnection? _clientConnection;
        private readonly IListener<ISimpleNetworkConnection> _listener;
        private readonly bool _isIPv6;
        private ISimpleNetworkConnection? _serverConnection;

        private readonly bool _tls;

        public TcpNetworkConnectionReadWriteTests(bool tls, AddressFamily addressFamily)
        {
            _tls = tls;
            _isIPv6 = addressFamily == AddressFamily.InterNetworkV6;

            SslServerAuthenticationOptions? serverAuthenticationOptions = null;
            if (_tls)
            {
                serverAuthenticationOptions = new SslServerAuthenticationOptions
                {
                    ClientCertificateRequired = false,
                    ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
                };
            }

            IServerTransport<ISimpleNetworkConnection> serverTransport = new TcpServerTransport();

            string host = _isIPv6 ? "[::1]" : "127.0.0.1";

            _listener = serverTransport.Listen(
                $"icerpc://{host}:0",
                serverAuthenticationOptions,
                LogAttributeLoggerFactory.Instance.Logger);
        }

        [SetUp]
        public async Task SetupAsync()
        {
            Task<ISimpleNetworkConnection> acceptTask = _listener.AcceptAsync();

            SslClientAuthenticationOptions? clientAuthenticationOptions = null;
            if (_tls)
            {
                clientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback =
                                CertificateValidaton.GetServerCertificateValidationCallback(
                                    certificateAuthorities: new X509Certificate2Collection()
                                    {
                                        new X509Certificate2("../../../certs/cacert.pem")
                                    }),
                    TargetHost = _isIPv6 ? "[::1]" : "127.0.0.1"
                };
            }

            IClientTransport<ISimpleNetworkConnection> clientTransport = new TcpClientTransport();

            _clientConnection = clientTransport.CreateConnection(
                _listener.Endpoint,
                clientAuthenticationOptions,
                LogAttributeLoggerFactory.Instance.Logger);
            Task<NetworkConnectionInformation> connectTask = _clientConnection.ConnectAsync(default);

            _serverConnection = await acceptTask;
            _ = await _serverConnection.ConnectAsync(default);
            _ = await connectTask;
        }

        [TearDown]
        public async Task TearDown()
        {
            if (_clientConnection is INetworkConnection clientConnection)
            {
                await clientConnection.DisposeAsync();
            }
            if (_serverConnection is INetworkConnection serverConnection)
            {
                await serverConnection.DisposeAsync();
            }
        }

        [OneTimeTearDown]
        public Task Shutdown() => _listener.DisposeAsync().AsTask();

        [Test]
        public async Task TcpNetworkConnectionReadWrite_LastActivity()
        {
            TimeSpan lastActivity = _clientConnection!.LastActivity;
            await Task.Delay(2);
            await ClientConnection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
            Assert.That(_clientConnection!.LastActivity, Is.GreaterThan(lastActivity));

            lastActivity = _serverConnection!.LastActivity;
            await Task.Delay(2);
            await ServerConnection.ReadAsync(new byte[1], default);
            Assert.That(_serverConnection!.LastActivity, Is.GreaterThan(lastActivity));
        }

        [Test]
        public void TcpNetworkConnectionReadWrite_ReadAsync_Cancellation()
        {
            using var canceled = new CancellationTokenSource();
            ValueTask<int> readTask = ClientConnection.ReadAsync(new byte[1], canceled.Token);
            Assert.That(readTask.IsCompleted, Is.False);
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await readTask);
        }

        [Test]
        public async Task TcpNetworkConnectionReadWrite_ReadAsync_ReturnsZero()
        {
            await _serverConnection!.DisposeAsync();
            int read = await ClientConnection.ReadAsync(new byte[1], default);

            Assert.That(read, Is.Zero);
        }

        [Test]
        public async Task TcpNetworkConnectionReadWrite_ReadAsync_DisposeAsync()
        {
            await _clientConnection!.DisposeAsync();
            Assert.CatchAsync<ObjectDisposedException>(async () => await ClientConnection.ReadAsync(new byte[1], default));
        }

        [Test]
        public void TcpNetworkConnectionReadWrite_ReadAsync_Exception()
        {
            Assert.ThrowsAsync<ArgumentException>(
                async () => await ClientConnection.ReadAsync(Array.Empty<byte>(), default));

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientConnection.ReadAsync(new byte[1], canceled.Token));
        }

        [Test]
        public async Task TcpNetworkConnectionReadWrite_WriteAsync_CancellationAsync()
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
                writeTask = ClientConnection.WriteAsync(_oneMBWriteBuffer, canceled.Token).AsTask();
                await Task.WhenAny(Task.Delay(500), writeTask);
            }
            while (writeTask.IsCompleted);

            // Cancel the blocked SendAsync and ensure OperationCanceledException is raised.
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await writeTask);
        }

        [Test]
        public async Task TcpNetworkConnectionReadWrite_WriteAsync_ConnectionLostException()
        {
            await _serverConnection!.DisposeAsync();
            Assert.CatchAsync<ConnectionLostException>(
                async () =>
                {
                    while (true)
                    {
                        await ClientConnection.WriteAsync(_oneMBWriteBuffer, default);
                    }
                });
        }

        [Test]
        public async Task TcpNetworkConnectionReadWrite_WriteAsync_DisposeAsync()
        {
            await _clientConnection!.DisposeAsync();
            Assert.CatchAsync<ObjectDisposedException>(async () => await ClientConnection.WriteAsync(_oneBWriteBuffer, default));
        }

        [Test]
        public void TcpNetworkConnectionReadWrite_WriteAsync_Exception()
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await ClientConnection.WriteAsync(_oneBWriteBuffer, canceled.Token));
        }

        [TestCase(1)]
        [TestCase(1024)]
        [TestCase(16 * 1024)]
        [TestCase(512 * 1024)]
        public async Task TcpNetworkConnectionReadWrite_ReadWriteAsync(int size)
        {
            byte[] writeBuffer = new byte[size];

            ValueTask test1 = Test(ClientConnection, ServerConnection);
            ValueTask test2 = Test(ServerConnection, ClientConnection);

            await test1;
            await test2;

            async ValueTask Test(ISimpleNetworkConnection connection1, ISimpleNetworkConnection connection2)
            {
                ValueTask writeTask = connection1.WriteAsync(new ReadOnlyMemory<byte>[] { writeBuffer }, default);
                Memory<byte> readBuffer = new byte[size];
                int offset = 0;
                while (offset < size)
                {
                    offset += await connection2.ReadAsync(readBuffer[offset..], default);
                }
            }
        }
    }
}
