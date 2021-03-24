// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    /// <summary>Test fixture for tests that need to test sockets. The constructor initialize a communicator and an
    // Server and setup client/server endpoints for a configurable protocol/transport/security.<summary>
    public class SocketBaseTest
    {
        private protected SslClientAuthenticationOptions? ClientAuthenticationOptions =>
            IsSecure ? ClientConnectionOptions.AuthenticationOptions : null;
        private protected Endpoint ClientEndpoint { get; }
        private protected ILogger Logger { get; }
        protected IncomingConnectionOptions ServerConnectionOptions => _server.ConnectionOptions;
        private protected bool IsSecure { get; }
        protected OutgoingConnectionOptions ClientConnectionOptions => _communicator.ConnectionOptions;
        private protected SslServerAuthenticationOptions? ServerAuthenticationOptions =>
            IsSecure ? ServerConnectionOptions.AuthenticationOptions : null;
        private protected Endpoint ServerEndpoint { get; }
        private protected string TransportName { get; }

        private IAcceptor? _acceptor;
        private readonly AsyncSemaphore _acceptSemaphore = new(1);
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly ILoggerFactory _loggerFactory;
        // Protects the _acceptor data member
        private readonly object _mutex = new();
        private static int _nextBasePort;

        public SocketBaseTest(Protocol protocol, string transport, bool secure)
        {
            int port = 11000;
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.Internal.BasePort"))
            {
                port = int.Parse(TestContext.Parameters["IceRpc.Tests.Internal.BasePort"]!);
            }
            port += Interlocked.Add(ref _nextBasePort, 1);

            TransportName = transport;
            IsSecure = secure;

            _loggerFactory = LoggerFactory.Create(
                builder =>
                {
                    builder.AddConsole(configure => configure.LogToStandardErrorThreshold = LogLevel.Debug);
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

            _communicator = new Communicator(connectionOptions: new()
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
                PreferNonSecure = IsSecure ? NonSecure.Never : NonSecure.Always
            });

            _server = new Server(_communicator, new ServerOptions
            {
                ConnectionOptions = new()
                {
                    AcceptNonSecure = secure ? NonSecure.Never : NonSecure.Always,
                    AuthenticationOptions = new()
                    {
                        ClientCertificateRequired = false,
                        ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
                    }
                },
                LoggerFactory = _loggerFactory
            });

            if (transport == "colocated")
            {
                ClientEndpoint = new ColocatedEndpoint(_server);
                ServerEndpoint = ClientEndpoint;
            }
            else
            {
                if (protocol == Protocol.Ice2)
                {
                    string endpoint = $"ice+{transport}://127.0.0.1:{port}";
                    ServerEndpoint = UriParser.ParseEndpoints(endpoint, _communicator, serverEndpoints: true)[0];
                    ClientEndpoint = UriParser.ParseEndpoints(endpoint, _communicator, serverEndpoints: false)[0];
                }
                else
                {
                    string endpoint = $"{transport} -h 127.0.0.1 -p {port}";
                    ServerEndpoint = Ice1Parser.ParseEndpoints(endpoint, _communicator, serverEndpoints: true)[0];
                    ClientEndpoint = Ice1Parser.ParseEndpoints(endpoint, _communicator, serverEndpoints: false)[0];
                }
            }
        }

        [OneTimeTearDown]
        public async Task ShutdownAsync()
        {
            _acceptor?.Dispose();
            await _communicator.DisposeAsync();
            await _server.DisposeAsync();
            _loggerFactory.Dispose();
        }

        protected IAcceptor CreateAcceptor() => ServerEndpoint.Acceptor(_server);

        protected async Task<MultiStreamSocket> ConnectAsync() => (await ConnectAndGetProxyAsync()).Socket;

        protected async Task<(MultiStreamSocket Socket, IServicePrx Proxy)> ConnectAndGetProxyAsync()
        {
            lock (_mutex)
            {
                _acceptor ??= CreateAcceptor();
            }

            Connection connection = await ClientEndpoint.ConnectAsync(
                ClientConnectionOptions,
                Logger,
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
            var options = new ServicePrxOptions()
            {
                Communicator = _communicator,
                Connection = connection,
                IsFixed = true,
                Path = "dummy",
                Protocol = ClientEndpoint.Protocol
            };
            return (connection.Socket, IServicePrx.Factory.Create(options));
        }

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
            catch(Exception ex)
            {
                Console.Error.WriteLine(ex);
                throw;
            }
            finally
            {
                _acceptSemaphore.Release();
            }
        }
    }
}
