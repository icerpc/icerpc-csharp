// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Internal
{
    /// <summary>Test fixture for tests that need to test socket connections.</summary>
    public class SocketConnectionBaseTest
    {
        protected static readonly byte[] OneBSendBuffer = new byte[1];
        protected static readonly byte[] OneMBSendBuffer = new byte[1024 * 1024];
        private protected Endpoint ClientEndpoint { get; }
        private protected ILogger Logger { get; }
        private protected Endpoint ServerEndpoint { get; }
        private protected string TransportName { get; }

        private IListener? _listener;
        private readonly AsyncSemaphore _acceptSemaphore = new(1);
        private readonly SslClientAuthenticationOptions _clientAuthenticationOptions;
        // Protects the _listener data member
        private readonly object _mutex = new();
        private readonly SslServerAuthenticationOptions _serverAuthenticationOptions;

        private static int _nextBasePort;

        public SocketConnectionBaseTest(
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

            TransportName = transport;
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

            Logger = LogAttributeLoggerFactory.Instance.CreateLogger("IceRpc");

            if (transport == "coloc")
            {
                ClientEndpoint = new Endpoint(Protocol.Ice2,
                                              transport,
                                              host: Guid.NewGuid().ToString(),
                                              port: 4062,
                                              ImmutableList<EndpointParam>.Empty);
                ServerEndpoint = ClientEndpoint;
            }
            else
            {
                string tlsOption = "";
                if (transport == "tcp" && tls != null)
                {
                    tlsOption = $"?tls={tls}";
                }
                string host = isIPv6 ? "[::1]" : "127.0.0.1";
                string endpoint = serverEndpoint?.Invoke(host, port) ?? $"ice+{transport}://{host}:{port}{tlsOption}";
                ServerEndpoint = endpoint;
                endpoint = clientEndpoint?.Invoke(host, port) ?? $"ice+{transport}://{host}:{port}{tlsOption}";
                ClientEndpoint = endpoint;
            }
        }

        [OneTimeTearDown]
        public void Shutdown() => _listener?.Dispose();

        protected static async ValueTask<NetworkSocket> SocketConnectionAsync(
            Task<INetworkConnection> connection) => (await connection as SocketConnection)!.NetworkSocket;

        protected async Task<INetworkConnection> AcceptAsync()
        {
            lock (_mutex)
            {
                _listener ??= CreateListener();
            }

            await _acceptSemaphore.EnterAsync();
            try
            {
                INetworkConnection networkConnection = await _listener.AcceptAsync();
                await networkConnection.ConnectAsync(default);
                if (ClientEndpoint.ParseTcpParams().Tls == null)
                {
                    // If the accepted connection is not secured, we need to read the first byte from the connection.
                    // See above for the reason.
                    if (networkConnection is SocketConnection connection)
                    {
                        Memory<byte> buffer = new byte[1];
                        await connection.NetworkSocket.ReceiveAsync(buffer, default);
                    }
                }
                return networkConnection;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                throw;
            }
            finally
            {
                _acceptSemaphore.Release();
            }
        }

        protected async Task<INetworkConnection> ConnectAsync()
        {
            if (ClientEndpoint.Transport != "udp")
            {
                lock (_mutex)
                {
                    _listener ??= CreateListener();
                }
            }

            IClientTransport clientTransport = TestHelper.CreateClientTransport(
                ClientEndpoint,
                authenticationOptions: _clientAuthenticationOptions);

            INetworkConnection networkConnection = clientTransport.CreateConnection(
                    ClientEndpoint,
                    LogAttributeLoggerFactory.Instance);
            await networkConnection.ConnectAsync(default);
            if (ClientEndpoint.Protocol == Protocol.Ice2 && !IsSecure)
            {
                // If establishing a non-secure Ice2 connection, we need to send a single byte. The peer peeks
                // a single byte over the connection to figure out if the client establishes a secure/non-secure
                // connection. If we were not providing this byte, the AcceptAsync from the peer would hang
                // indefinitely.
                if (networkConnection is SocketConnection connection)
                {
                    await connection.NetworkSocket.SendAsync(new byte[1] { 0 }, default);
                }
            }

            if (networkConnection.RemoteEndpoint!.Transport != TransportName)
            {
                Debug.Assert(TransportName == "coloc");
                Debug.Assert(networkConnection is ColocConnection);
            }
            return networkConnection;
        }

        protected IListener CreateListener(TcpOptions? options = null, Endpoint? serverEndpoint = null) =>
            TestHelper.CreateServerTransport(
                serverEndpoint ?? ServerEndpoint,
                options: options,
                multiStreamOptions: null,
                _serverAuthenticationOptions).Listen(
                    serverEndpoint ?? ServerEndpoint,
                    LogAttributeLoggerFactory.Instance).Listener!;

        protected INetworkConnection CreateServerConnection() =>
            TestHelper.CreateServerTransport(
                ServerEndpoint,
                options: null,
                multiStreamOptions: null,
                authenticationOptions: _serverAuthenticationOptions).Listen(
                    ServerEndpoint,
                    LogAttributeLoggerFactory.Instance).Connection!;

        protected INetworkConnection CreateClientConnection() =>
            TestHelper.CreateClientTransport(
                ClientEndpoint,
                options: null,
                multiStreamOptions: null,
                _clientAuthenticationOptions).CreateConnection(
                    ClientEndpoint,
                    LogAttributeLoggerFactory.Instance);
    }
}
