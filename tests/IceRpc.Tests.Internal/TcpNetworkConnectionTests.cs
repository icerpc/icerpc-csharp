// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    [TestFixture(false, AddressFamily.InterNetwork)]
    [TestFixture(true, AddressFamily.InterNetwork)]
    [TestFixture(null, AddressFamily.InterNetwork)]
    [TestFixture(false, AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class TcpNetworkConnectionTests
    {
        private readonly IClientTransport<ISimpleNetworkConnection> _clientTransport;
        private readonly Endpoint _endpoint;
        private readonly IServerTransport<ISimpleNetworkConnection> _serverTransport;

        private readonly bool? _tls;

        public TcpNetworkConnectionTests(bool? tls, AddressFamily addressFamily)
        {
            _tls = tls;
            bool isIPv6 = addressFamily == AddressFamily.InterNetworkV6;
            string host = isIPv6 ? "[::1]" : "127.0.0.1";

            var clientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection()
                                {
                                    new X509Certificate2("../../../certs/cacert.pem")
                                }),
                TargetHost = host
            };

            _clientTransport = new TcpClientTransport(clientAuthenticationOptions);

            var serverAuthenticationOptions = new SslServerAuthenticationOptions
            {
                ClientCertificateRequired = false,
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
            };

            _serverTransport = new TcpServerTransport(serverAuthenticationOptions);

            string tlsString = "";
            if (tls != null)
            {
                tlsString = $"?tls={tls}";
            }

            _endpoint = $"ice+tcp://{host}:0{tlsString}";
        }

        [Test]
        public async Task TcpNetworkConnection_Listener_AcceptAsync()
        {
            using IListener<ISimpleNetworkConnection> listener = CreateListener(_endpoint);
            ISimpleNetworkConnection clientConnection = CreateClientConnection(listener.Endpoint);

            Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
            var connectTask = clientConnection.ConnectAsync(default);

            ISimpleNetworkConnection serverConnection = await acceptTask;
            _ = await serverConnection.ConnectAsync(default);
            _ = await connectTask;

            clientConnection.Close();
            serverConnection.Close();
        }

        [Test]
        public void TcpNetworkConnection_Listener_TransportException()
        {
            using IListener<ISimpleNetworkConnection> listener = CreateListener(_endpoint);
            Assert.Throws<TransportException>(
                () => _serverTransport.Listen(listener.Endpoint, LogAttributeLoggerFactory.Instance));
        }

        [Test]
        public async Task TcpNetworkConnection_AcceptAsync()
        {
            using IListener<ISimpleNetworkConnection> listener = CreateListener(_endpoint);

            Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();

            ISimpleNetworkConnection clientConnection = CreateClientConnection(listener.Endpoint);

            Task<(ISimpleStream, NetworkConnectionInformation)> connectTask = clientConnection.ConnectAsync(default);

            ISimpleNetworkConnection serverConnection = await acceptTask;

            Task<(ISimpleStream, NetworkConnectionInformation)> serverConnectTask =
                serverConnection.ConnectAsync(default);

            ISimpleStream clientStream = (await connectTask).Item1;

            if (_tls == null)
            {
                await clientStream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
            }

            _ = await serverConnectTask;

            clientConnection.Close();
            serverConnection.Close();
        }

        [Test]
        public async Task TcpNetworkConnection_AcceptAsync_ConnectionLostExceptionAsync()
        {
            using IListener<ISimpleNetworkConnection> listener = CreateListener(_endpoint);

            Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();

            ISimpleNetworkConnection clientConnection = CreateClientConnection(listener.Endpoint);

            Socket clientSocket = ((TcpNetworkConnection)clientConnection).Socket;

            // We don't use clientConnection.ConnectAsync() here as this would start the TLS handshake for secure
            // connections
            await clientSocket.ConnectAsync(
                new DnsEndPoint(listener.Endpoint.Host, listener.Endpoint.Port)).ConfigureAwait(false);

            ISimpleNetworkConnection serverConnection = await acceptTask;
            clientConnection.Close();

            AsyncTestDelegate testDelegate;
            if (_tls == false)
            {
                // Server side ConnectAsync is a no-op for non secure TCP connections so it won't throw.
                ISimpleStream serverStream = (await serverConnection.ConnectAsync(default)).Item1;
                testDelegate = async () => await serverStream.ReadAsync(new byte[1], default);
            }
            else
            {
                testDelegate = async () => await serverConnection.ConnectAsync(default);
            }
            Assert.ThrowsAsync<ConnectionLostException>(testDelegate);
            serverConnection.Close();
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void TcpNetworkConnection_Listener_AddressReuse(bool wildcard1, bool wildcard2)
        {
            using IListener<ISimpleNetworkConnection> listener = wildcard1 ?
                CreateListener(_endpoint with { Host = "::0" }) : CreateListener(_endpoint);

            Endpoint endpoint = listener.Endpoint with { Host = _endpoint.Host };

            if (wildcard2)
            {
                Endpoint serverEndpoint = endpoint with { Host = "::0" };
                if (OperatingSystem.IsMacOS())
                {
                    // On macOS, it's still possible to bind to a specific address even if a connection is bound
                    // to the wildcard address.
                    Assert.DoesNotThrow(() => CreateListener(serverEndpoint).Dispose());
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
                    Assert.DoesNotThrow(() => CreateListener(endpoint).Dispose());
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
            using IListener<ISimpleNetworkConnection> listener = CreateListener(_endpoint);

            ISimpleNetworkConnection clientConnection = CreateClientConnection(listener.Endpoint);

            var connectTask = clientConnection.ConnectAsync(default);

            ISimpleNetworkConnection serverConnection = await listener.AcceptAsync();

            using var source = new CancellationTokenSource();
            source.Cancel();
            var serverConnectTask = serverConnection.ConnectAsync(source.Token);

            if (_tls == false)
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
        public void TcpNetworkConnection_ConnectAsync_OperationCanceledException()
        {
            using IListener<ISimpleNetworkConnection> listener = CreateListener(_endpoint);

            using var source = new CancellationTokenSource();
            if (_tls == false)
            {
                // ConnectAsync might complete synchronously with TCP
            }
            else
            {
                ISimpleNetworkConnection clientConnection = CreateClientConnection(listener.Endpoint);

                var connectTask = clientConnection.ConnectAsync(source.Token);
                source.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await connectTask);

                clientConnection.Close();
            }

            using var source2 = new CancellationTokenSource();
            source2.Cancel();
            ISimpleNetworkConnection clientConnection2 = CreateClientConnection(listener.Endpoint);

            Assert.CatchAsync<OperationCanceledException>(
                async () => await clientConnection2.ConnectAsync(source2.Token));
        }

        private ISimpleNetworkConnection CreateClientConnection(Endpoint endpoint) =>
            _clientTransport.CreateConnection(endpoint, LogAttributeLoggerFactory.Instance);

        private IListener<ISimpleNetworkConnection> CreateListener(Endpoint endpoint) =>
            _serverTransport.Listen(endpoint, LogAttributeLoggerFactory.Instance);

    }
}
