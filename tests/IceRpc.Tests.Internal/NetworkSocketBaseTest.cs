// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Globalization;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    public class NetworkSocketBaseTest
    {
        protected static readonly ReadOnlyMemory<ReadOnlyMemory<byte>> OneBSendBuffer =
            new ReadOnlyMemory<byte>[] { new byte[1] };
        protected static readonly ReadOnlyMemory<ReadOnlyMemory<byte>> OneMBSendBuffer =
            new ReadOnlyMemory<byte>[] { new byte[1024 * 1024] };
        protected Endpoint ClientEndpoint { get; }
        protected Endpoint ServerEndpoint { get; }

        private readonly AsyncSemaphore _acceptSemaphore = new(1);
        private readonly SslClientAuthenticationOptions _clientAuthenticationOptions;
        // Protects the _listener data member
        private IListener? _listener;
        private readonly object _mutex = new();
        private static int _nextBasePort;
        private readonly SslServerAuthenticationOptions _serverAuthenticationOptions;

        public NetworkSocketBaseTest(
            string transport,
            bool? tls,
            AddressFamily addressFamily = AddressFamily.InterNetwork,
            Func<string, int, string>? clientEndpoint = null,
            Func<string, int, string>? serverEndpoint = null)
        {
            int port = 11000;
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.Internal.BasePort"))
            {
                port = int.Parse(TestContext.Parameters["IceRpc.Tests.Internal.BasePort"]!,
                                 CultureInfo.InvariantCulture.NumberFormat);
            }
            port += Interlocked.Add(ref _nextBasePort, 1);

            bool isIPv6 = addressFamily == AddressFamily.InterNetworkV6;

            _clientAuthenticationOptions = new()
            {
                RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection()
                                {
                                    new X509Certificate2("../../../certs/cacert.pem")
                                }),
                TargetHost = isIPv6 ? "[::1]" : "127.0.0.1"
            };

            _serverAuthenticationOptions = new()
            {
                ClientCertificateRequired = false,
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
            };

            string host = isIPv6 ? "[::1]" : "127.0.0.1";
            string endpoint;
            if (transport == "udp")
            {
                endpoint = $"udp -h {host} -p {port}";
            }
            else
            {
                string tlsOption = "";
                if (transport == "tcp" && tls != null)
                {
                    tlsOption = $"?tls={tls}";
                }
                endpoint = $"ice+{transport}://{host}:{port}{tlsOption}";
            }
            ServerEndpoint = serverEndpoint?.Invoke(host, port) ?? endpoint;
            ClientEndpoint = clientEndpoint?.Invoke(host, port) ?? endpoint;
        }

        [OneTimeTearDown]
        public void Shutdown() => _listener?.Dispose();

        protected async Task<NetworkSocket> AcceptAsync()
        {
            lock (_mutex)
            {
                _listener ??= CreateListener();
            }

            await _acceptSemaphore.EnterAsync();
            try
            {
                INetworkConnection networkConnection = await _listener.AcceptAsync();

                NetworkSocket networkSocket = GetNetworkSocket(networkConnection);
                await networkSocket.ConnectAsync(ServerEndpoint, default);

                if (ClientEndpoint.Transport == "tcp" && ClientEndpoint.ParseTcpParams().Tls == null)
                {
                    // If Tls was negotiated on connection establishment, we need to read the byte sent by the
                    // client (see below).
                    Memory<byte> buffer = new byte[1];
                    await networkSocket.ReceiveAsync(buffer, default);
                }
                return networkSocket;
            }
            finally
            {
                _acceptSemaphore.Release();
            }
        }

        protected async Task<NetworkSocket> ConnectAsync()
        {
            if (ClientEndpoint.Transport != "udp")
            {
                lock (_mutex)
                {
                    _listener ??= CreateListener();
                }
            }

            NetworkSocket networkSocket = CreateClientNetworkSocket();
            await networkSocket.ConnectAsync(ClientEndpoint, default);

            // If Tls is negotiated on connection establishment, we need to send a single byte. The peer peeks
            // a single byte over the connection to figure out if the client establishes a secure/non-secure
            // connection. If we wouldn't providing this byte, the AcceptAsync from the peer would hang
            // indefinitely.
            if (ClientEndpoint.Transport == "tcp" && ClientEndpoint.ParseTcpParams().Tls == null)
            {
                await networkSocket.SendAsync(new ReadOnlyMemory<byte>[] { new byte[1] { 0 } }, default);
            }
            return networkSocket;
        }

        protected IListener CreateListener(TcpOptions? options = null, Endpoint? serverEndpoint = null) =>
            TestHelper.CreateServerTransport(
                serverEndpoint ?? ServerEndpoint,
                options: options,
                multiStreamOptions: null,
                _serverAuthenticationOptions).Listen(serverEndpoint ?? ServerEndpoint);

        protected async ValueTask<NetworkSocket> CreateServerNetworkSocketAsync() =>
            GetNetworkSocket(await TestHelper.CreateServerTransport(
                ServerEndpoint,
                authenticationOptions: _serverAuthenticationOptions).Listen(ServerEndpoint).AcceptAsync());

        protected NetworkSocket CreateClientNetworkSocket() =>
            GetNetworkSocket(TestHelper.CreateClientTransport(
                ClientEndpoint,
                authenticationOptions: _clientAuthenticationOptions).CreateConnection(ClientEndpoint));

        protected static NetworkSocket GetNetworkSocket(INetworkConnection connection) =>
            ((SocketNetworkConnection)connection).NetworkSocket;
    }
}
