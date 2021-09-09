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
    /// <summary>Test fixture for tests that need to test connections. The constructor initialize a communicator and an
    /// Server and setup client/server endpoints for a configurable protocol/transport/security.</summary>
    public class ConnectionBaseTest
    {
        protected static readonly byte[] OneBSendBuffer = new byte[1];
        protected static readonly byte[] OneMBSendBuffer = new byte[1024 * 1024];
        private protected Endpoint ClientEndpoint { get; }
        private protected ILogger Logger { get; }
        private protected bool IsIPv6 { get; }
        private protected bool IsSecure { get; }
        private protected Endpoint ServerEndpoint { get; }
        private protected MultiStreamOptions? ServerMultiStreamOptions { get; set; }
        private protected string TransportName { get; }

        private IListener? _listener;
        private readonly AsyncSemaphore _acceptSemaphore = new(1);
        private readonly SslClientAuthenticationOptions _clientAuthenticationOptions;
        // Protects the _listener data member
        private readonly object _mutex = new();
        private readonly SslServerAuthenticationOptions _serverAuthenticationOptions;

        private static int _nextBasePort;

        public ConnectionBaseTest(
            Protocol protocol,
            string transport,
            bool tls,
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
            IsSecure = tls;
            IsIPv6 = addressFamily == AddressFamily.InterNetworkV6;

            _clientAuthenticationOptions = new()
            {
                RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection()
                                {
                                    new X509Certificate2("../../../certs/cacert.pem")
                                }),
                TargetHost = IsIPv6 ? "[::1]" : "127.0.0.1"
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
                                              Host: Guid.NewGuid().ToString(),
                                              Port: 4062,
                                              ImmutableList<EndpointParam>.Empty);
                ServerEndpoint = ClientEndpoint;
            }
            else
            {
                if (protocol == Protocol.Ice2)
                {
                    string tlsOption = "";
                    if (transport == "tcp" && !IsSecure)
                    {
                        tlsOption = "?tls=false";
                    }

                    string host = IsIPv6 ? "[::1]" : "127.0.0.1";
                    string endpoint = serverEndpoint?.Invoke(host, port) ??
                        $"ice+{transport}://{host}:{port}{tlsOption}";
                    ServerEndpoint = endpoint;
                    endpoint = clientEndpoint?.Invoke(host, port) ?? $"ice+{transport}://{host}:{port}{tlsOption}";
                    ClientEndpoint = endpoint;
                }
                else
                {
                    string host = IsIPv6 ? "\"::1\"" : "127.0.0.1";
                    string endpoint = serverEndpoint?.Invoke(host, port) ?? $"{transport} -h {host} -p {port}";
                    ServerEndpoint = endpoint;
                    endpoint = clientEndpoint?.Invoke(host, port) ?? $"{transport} -h {host} -p {port}";
                    ClientEndpoint = endpoint;
                }
            }
        }

        [OneTimeTearDown]
        public void Shutdown() => _listener?.Dispose();

        protected static async ValueTask<NetworkSocket> NetworkSocketConnectionAsync(
            Task<MultiStreamConnection> connection) => (await connection as NetworkSocketConnection)!.NetworkSocket;

        protected async Task<MultiStreamConnection> AcceptAsync()
        {
            lock (_mutex)
            {
                _listener ??= CreateListener();
            }

            await _acceptSemaphore.EnterAsync();
            try
            {
                MultiStreamConnection multiStreamConnection = await _listener.AcceptAsync();
                await multiStreamConnection.ConnectAsync(default);
                if (ClientEndpoint.Protocol == Protocol.Ice2 && !multiStreamConnection.IsSecure)
                {
                    // If the accepted connection is not secured, we need to read the first byte from the connection.
                    // See above for the reason.
                    if (multiStreamConnection is NetworkSocketConnection connection)
                    {
                        Memory<byte> buffer = new byte[1];
                        await connection.NetworkSocket.ReceiveAsync(buffer, default);
                    }
                }
                return multiStreamConnection;
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

        protected async Task<MultiStreamConnection> ConnectAsync()
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

            MultiStreamConnection multiStreamConnection = clientTransport.CreateConnection(
                    ClientEndpoint,
                    LogAttributeLoggerFactory.Instance);
            await multiStreamConnection.ConnectAsync(default);
            if (ClientEndpoint.Protocol == Protocol.Ice2 && !IsSecure)
            {
                // If establishing a non-secure Ice2 connection, we need to send a single byte. The peer peeks
                // a single byte over the connection to figure out if the client establishes a secure/non-secure
                // connection. If we were not providing this byte, the AcceptAsync from the peer would hang
                // indefinitely.
                if (multiStreamConnection is NetworkSocketConnection connection)
                {
                    await connection.NetworkSocket.SendAsync(new byte[1] { 0 }, default);
                }
            }

            if (multiStreamConnection.RemoteEndpoint!.Transport != TransportName)
            {
                Debug.Assert(TransportName == "coloc");
                Debug.Assert(multiStreamConnection is ColocConnection);
            }
            return multiStreamConnection;
        }

        protected IListener CreateListener(TcpOptions? options = null, Endpoint? serverEndpoint = null) =>
            TestHelper.CreateServerTransport(
                serverEndpoint ?? ServerEndpoint,
                options: options,
                ServerMultiStreamOptions ?? null,
                _serverAuthenticationOptions).Listen(
                    serverEndpoint ?? ServerEndpoint,
                    LogAttributeLoggerFactory.Instance).Listener!;

        protected MultiStreamConnection CreateServerConnection() =>
            TestHelper.CreateServerTransport(
                ServerEndpoint,
                options: null,
                ServerMultiStreamOptions ?? null,
                authenticationOptions: _serverAuthenticationOptions).Listen(
                    ServerEndpoint,
                    LogAttributeLoggerFactory.Instance).Connection!;

        protected MultiStreamConnection CreateClientConnection() =>
            TestHelper.CreateClientTransport(
                ClientEndpoint,
                options: null,
                multiStreamOptions: null,
                _clientAuthenticationOptions).CreateConnection(
                    ClientEndpoint,
                    LogAttributeLoggerFactory.Instance);
    }
}
