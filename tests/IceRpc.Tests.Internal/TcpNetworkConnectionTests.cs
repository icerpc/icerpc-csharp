// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Net;
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
    public class TcpNetworkConnectionTests
    {
        private readonly SslClientAuthenticationOptions? _clientAuthenticationOptions;
        private readonly IClientTransport<ISimpleNetworkConnection> _clientTransport;
        private readonly Endpoint _endpoint;
        private readonly SslServerAuthenticationOptions? _serverAuthenticationOptions;
        private readonly IServerTransport<ISimpleNetworkConnection> _serverTransport;

        public TcpNetworkConnectionTests(bool tls, AddressFamily addressFamily)
        {
            bool isIPv6 = addressFamily == AddressFamily.InterNetworkV6;
            string host = isIPv6 ? "[::1]" : "127.0.0.1";

            if (tls)
            {
                _clientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback =
                                CertificateValidaton.GetServerCertificateValidationCallback(
                                    certificateAuthorities: new X509Certificate2Collection()
                                    {
                                        new X509Certificate2("../../../certs/cacert.pem")
                                    }),
                    TargetHost = host
                };

                _serverAuthenticationOptions = new SslServerAuthenticationOptions
                {
                    ClientCertificateRequired = false,
                    ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
                };
            }

            _clientTransport = new TcpClientTransport();
            _serverTransport = new TcpServerTransport();
            _endpoint = $"icerpc://{host}:0";
        }

        [Test]
        public async Task TcpNetworkConnection_Listener_AcceptAsync()
        {
            await using IListener<ISimpleNetworkConnection> listener = CreateListener(_endpoint);
            await using ISimpleNetworkConnection clientConnection = CreateClientConnection(listener.Endpoint);

            Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
            var connectTask = clientConnection.ConnectAsync(default);

            await using ISimpleNetworkConnection serverConnection = await acceptTask;
            _ = await serverConnection.ConnectAsync(default);
            _ = await connectTask;
        }

        [Test]
        public async Task TcpNetworkConnection_Listener_TransportException()
        {
            await using IListener<ISimpleNetworkConnection> listener = CreateListener(_endpoint);
            Assert.Throws<TransportException>(
                () => _serverTransport.Listen(
                    listener.Endpoint,
                    _serverAuthenticationOptions,
                    LogAttributeLoggerFactory.Instance.Logger));
        }

        [Test]
        public async Task TcpNetworkConnection_AcceptAsync()
        {
            await using IListener<ISimpleNetworkConnection> listener = CreateListener(_endpoint);

            Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();

            await using ISimpleNetworkConnection clientConnection = CreateClientConnection(listener.Endpoint);

            Task<NetworkConnectionInformation> connectTask = clientConnection.ConnectAsync(default);

            await using ISimpleNetworkConnection serverConnection = await acceptTask;

            Task<NetworkConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(default);

            _ = await connectTask;
            _ = await serverConnectTask;
        }

        [Test]
        public async Task TcpNetworkConnection_AcceptAsync_ConnectFailedExceptionAsync()
        {
            await using IListener<ISimpleNetworkConnection> listener = CreateListener(_endpoint);

            Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();

            ISimpleNetworkConnection clientConnection = CreateClientConnection(listener.Endpoint);

            Socket clientSocket = ((TcpNetworkConnection)clientConnection).Socket;

            // We don't use clientConnection.ConnectAsync() here as this would start the TLS handshake for secure
            // connections
            await clientSocket.ConnectAsync(
                new DnsEndPoint(listener.Endpoint.Host, listener.Endpoint.Port));

            await using ISimpleNetworkConnection serverConnection = await acceptTask;
            await clientConnection.DisposeAsync();

            if (_serverAuthenticationOptions == null)
            {
                // Server side ConnectAsync is a no-op for non secure TCP connections so it won't throw.
                _ = await serverConnection.ConnectAsync(default);

                Assert.ThrowsAsync<ConnectionLostException>(
                    async () => await serverConnection.ReadAsync(new byte[1], default));
            }
            else
            {
                Assert.ThrowsAsync<ConnectFailedException>(async () => await serverConnection.ConnectAsync(default));
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task TcpNetworkConnection_Listener_AddressReuse(bool wildcard1, bool wildcard2)
        {
            await using IListener<ISimpleNetworkConnection> listener = wildcard1 ?
                CreateListener(_endpoint with { Host = "::0" }) : CreateListener(_endpoint);

            Endpoint endpoint = listener.Endpoint with { Host = _endpoint.Host };

            if (wildcard2)
            {
                Endpoint serverEndpoint = endpoint with { Host = "::0" };
                if (OperatingSystem.IsMacOS())
                {
                    // On macOS, it's still possible to bind to a specific address even if a connection is bound
                    // to the wildcard address.
                    Assert.DoesNotThrowAsync(() => CreateListener(serverEndpoint).DisposeAsync().AsTask());
                }
                else
                {
                    Assert.Catch<TransportException>(() => CreateListener(serverEndpoint));
                }
            }
            else
            {
                if (wildcard1 && OperatingSystem.IsMacOS())
                {
                    // On macOS, it's still possible to bind to a specific address even if a connection is bound
                    // to the wildcard address.
                    Assert.DoesNotThrowAsync(() => CreateListener(endpoint).DisposeAsync().AsTask());
                }
                else
                {
                    Assert.Catch<TransportException>(() => CreateListener(endpoint));
                }
            }
        }

        [Test]
        public async Task TcpNetworkConnection_AcceptAsync_OperationCanceledExceptionAsync()
        {
            await using IListener<ISimpleNetworkConnection> listener = CreateListener(_endpoint);

            await using ISimpleNetworkConnection clientConnection = CreateClientConnection(listener.Endpoint);

            var connectTask = clientConnection.ConnectAsync(default);

            await using ISimpleNetworkConnection serverConnection = await listener.AcceptAsync();

            using var source = new CancellationTokenSource();
            source.Cancel();
            var serverConnectTask = serverConnection.ConnectAsync(source.Token);

            if (_serverAuthenticationOptions == null)
            {
                // Server-side ConnectionAsync is a no-op for non-secure TCP connections so it won't throw.
                await serverConnectTask;
            }
            else
            {
                Assert.CatchAsync<OperationCanceledException>(async () => await serverConnectTask);
            }
        }

        [Test]
        public async Task TcpNetworkConnection_ConnectAsync_OperationCanceledException()
        {
            await using IListener<ISimpleNetworkConnection> listener = CreateListener(_endpoint);

            using var source = new CancellationTokenSource();
            if (_clientAuthenticationOptions == null)
            {
                // ConnectAsync might complete synchronously with TCP
            }
            else
            {
                await using ISimpleNetworkConnection clientConnection = CreateClientConnection(listener.Endpoint);

                var connectTask = clientConnection.ConnectAsync(source.Token);
                source.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await connectTask);
            }

            using var source2 = new CancellationTokenSource();
            source2.Cancel();
            ISimpleNetworkConnection clientConnection2 = CreateClientConnection(listener.Endpoint);

            Assert.CatchAsync<OperationCanceledException>(
                async () => await clientConnection2.ConnectAsync(source2.Token));
        }

        private ISimpleNetworkConnection CreateClientConnection(Endpoint endpoint) =>
            _clientTransport.CreateConnection(
                endpoint,
                _clientAuthenticationOptions,
                LogAttributeLoggerFactory.Instance.Logger);

        private IListener<ISimpleNetworkConnection> CreateListener(Endpoint endpoint) =>
            _serverTransport.Listen(endpoint, _serverAuthenticationOptions, LogAttributeLoggerFactory.Instance.Logger);

    }
}
