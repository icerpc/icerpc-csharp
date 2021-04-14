// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    /// <summary>Test fixture for tests that need to test sockets. The constructor initialize a communicator and an
    /// Server and setup client/server endpoints for a configurable protocol/transport/security.</summary>
    public class SocketBaseTest
    {
        private protected Communicator Communicator { get; }
        private protected SslClientAuthenticationOptions? ClientAuthenticationOptions =>
            IsSecure ? ClientConnectionOptions.AuthenticationOptions : null;
        private protected Endpoint ClientEndpoint { get; }
        private protected ILogger Logger { get; }
        protected Server Server { get; }
        protected IncomingConnectionOptions ServerConnectionOptions => Server.ConnectionOptions;
        private protected bool IsIPv6 { get; }
        private protected bool IsSecure { get; }
        protected OutgoingConnectionOptions ClientConnectionOptions => Communicator.ConnectionOptions;
        private protected SslServerAuthenticationOptions? ServerAuthenticationOptions =>
            IsSecure ? ServerConnectionOptions.AuthenticationOptions : null;
        private protected Endpoint ServerEndpoint { get; }
        private protected string TransportName { get; }

        private IAcceptor? _acceptor;
        private readonly AsyncSemaphore _acceptSemaphore = new(1);
        private readonly ILoggerFactory _loggerFactory;
        // Protects the _acceptor data member
        private readonly object _mutex = new();
        private static int _nextBasePort;

        public SocketBaseTest(
            Protocol protocol,
            string transport,
            NonSecure nonSecure,
            AddressFamily addressFamily = AddressFamily.InterNetwork,
            Func<string, int, string>? clientEndpoint = null,
            Func<string, int, string>? serverEndpoint = null)
        {
            int port = 11000;
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.Internal.BasePort"))
            {
                port = int.Parse(TestContext.Parameters["IceRpc.Tests.Internal.BasePort"]!);
            }
            port += Interlocked.Add(ref _nextBasePort, 1);

            TransportName = transport;
            IsSecure = nonSecure == NonSecure.Never;
            IsIPv6 = addressFamily == AddressFamily.InterNetworkV6;

            _loggerFactory = LoggerFactory.Create(
                builder =>
                {
                    // builder.AddConsole(configure => configure.LogToStandardErrorThreshold = LogLevel.Debug);
                    // builder.AddSimpleConsole(configure => configure.IncludeScopes = true);
                    // builder.AddJsonConsole(configure =>
                    // {
                    //     configure.IncludeScopes = true;
                    //     configure.JsonWriterOptions = new System.Text.Json.JsonWriterOptions()
                    //     {
                    //         Indented = true
                    //     };
                    // });
                    builder.SetMinimumLevel(LogLevel.Debug);
                });

            Logger = _loggerFactory.CreateLogger("IceRPC");

            var clientConnectionOptions = new OutgoingConnectionOptions()
            {
                AuthenticationOptions = new()
                {
                    RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection()
                                {
                                    new X509Certificate2("../../../certs/cacert.pem")
                                })
                },
                NonSecure = IsSecure ? NonSecure.Never : NonSecure.Always
            };
            Communicator = new Communicator(connectionOptions: clientConnectionOptions);

            var serverConnectionOptions = new IncomingConnectionOptions()
            {
                AcceptNonSecure = nonSecure,
                AuthenticationOptions = new()
                {
                    ClientCertificateRequired = false,
                    ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
                }
            };
            Server = new Server
            {
                Communicator = Communicator,
                ConnectionOptions = serverConnectionOptions,
                LoggerFactory = _loggerFactory
            };

            if (transport == "colocated")
            {
                ClientEndpoint = new ColocatedEndpoint(Server);
                ServerEndpoint = ClientEndpoint;
            }
            else
            {
                if (protocol == Protocol.Ice2)
                {
                    string host = IsIPv6 ? "[::1]" : "127.0.0.1";
                    string endpoint = serverEndpoint?.Invoke(host, port) ?? $"ice+{transport}://{host}:{port}";
                    ServerEndpoint = Endpoint.Parse(endpoint);
                    endpoint = clientEndpoint?.Invoke(host, port) ?? $"ice+{transport}://{host}:{port}";
                    ClientEndpoint = Endpoint.Parse(endpoint);
                }
                else
                {
                    string host = IsIPv6 ? "\"::1\"" : "127.0.0.1";
                    string endpoint = serverEndpoint?.Invoke(host, port) ?? $"{transport} -h {host} -p {port}";
                    ServerEndpoint = Endpoint.Parse(endpoint);
                    endpoint = clientEndpoint?.Invoke(host, port) ?? $"{transport} -h {host} -p {port}";
                    ClientEndpoint = Endpoint.Parse(endpoint);
                }
            }
        }

        [OneTimeTearDown]
        public async Task ShutdownAsync()
        {
            _acceptor?.Dispose();
            await Communicator.DisposeAsync();
            await Server.DisposeAsync();
            _loggerFactory.Dispose();
        }

        static protected async ValueTask<SingleStreamSocket> SingleStreamSocketAsync(Task<MultiStreamSocket> socket) =>
            (await socket as MultiStreamOverSingleStreamSocket)!.Underlying;

        protected async Task<MultiStreamSocket> AcceptAsync()
        {
            lock (_mutex)
            {
                _acceptor ??= CreateAcceptor();
            }

            await _acceptSemaphore.EnterAsync();
            try
            {
                Connection connection = await _acceptor.AcceptAsync();
                Debug.Assert(connection.Endpoint.TransportName == TransportName);
                await connection.Socket.AcceptAsync(ServerAuthenticationOptions, default);
                if (ClientEndpoint.Protocol == Protocol.Ice2 && !connection.IsSecure)
                {
                    // If the accepted connection is not secured, we need to read the first byte from the socket.
                    // See above for the reason.
                    if (connection.Socket is MultiStreamOverSingleStreamSocket socket)
                    {
                        Memory<byte> buffer = new byte[1];
                        await socket.Underlying.ReceiveAsync(buffer, default);
                    }
                }
                return connection.Socket;
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

        protected async Task<MultiStreamSocket> ConnectAsync(OutgoingConnectionOptions? options = null) =>
            (await ConnectAndGetProxyAsync(options)).Socket;

        protected async Task<(MultiStreamSocket Socket, IServicePrx Proxy)> ConnectAndGetProxyAsync(
            OutgoingConnectionOptions? connectionOptions = null)
        {
            if (!ClientEndpoint.IsDatagram)
            {
                lock (_mutex)
                {
                    _acceptor ??= CreateAcceptor();
                }
            }

            Connection connection = await ClientEndpoint.ConnectAsync(
                connectionOptions ?? ClientConnectionOptions,
                Logger,
                default);
            if (ClientEndpoint.Protocol == Protocol.Ice2 && !IsSecure)
            {
                // If establishing a non-secure Ice2 connection, we need to send a single byte. The peer peeks
                // a single byte over the socket to figure out if the client establishes a secure/non-secure
                // connection. If we were not providing this byte, the AcceptAsync from the peer would hang
                // indefinitely.
                if (connection.Socket is MultiStreamOverSingleStreamSocket socket)
                {
                    var buffer = new List<ArraySegment<byte>>() { new byte[1] { 0 } };
                    await socket.Underlying.SendAsync(buffer, default);
                }
            }

            if (connection.Endpoint.TransportName != TransportName)
            {
                Debug.Assert(TransportName == "colocated");
                Debug.Assert(connection.Socket is ColocatedSocket);
            }
            var options = new ProxyOptions()
            {
                Communicator = Communicator,
            };
            return (connection.Socket, IServicePrx.Factory.Create("/dummy",
                                                                  ClientEndpoint.Protocol,
                                                                  ClientEndpoint.Protocol.GetEncoding(),
                                                                  endpoints: ImmutableList<Endpoint>.Empty,
                                                                  connection,
                                                                  options));
        }

        protected IAcceptor CreateAcceptor() => ServerEndpoint.Acceptor(Server);

        protected MultiStreamSocket CreateDatagramServerSocket() =>
            ServerEndpoint.CreateDatagramServerConnection(Server).Socket;
    }
}
