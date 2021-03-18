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
            IsSecure ? _clientCommunicator.AuthenticationOptions : null;
        private protected Endpoint ClientEndpoint { get; }
        private protected bool IsSecure { get; }
        private protected Server Server { get; }
        private protected SslServerAuthenticationOptions? ServerAuthenticationOptions =>
            IsSecure ? Server.AuthenticationOptions : null;
        private protected Endpoint ServerEndpoint { get; }
        private protected string TransportName { get; }

        private IAcceptor? _acceptor;
        private readonly AsyncSemaphore _acceptSemaphore = new(1);
        private readonly Communicator _clientCommunicator;

        // Protects the _acceptor data member
        private readonly object _mutex = new();
        private static int _nextBasePort;
        private readonly Communicator _serverCommunicator;

        public SocketBaseTest(
            Protocol protocol,
            string transport,
            bool secure,
            Action<ServerOptions>? serverOptionsBuilder = null)
        {
            int port = 11000;
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.Internal.BasePort"))
            {
                port = int.Parse(TestContext.Parameters["IceRpc.Tests.Internal.BasePort"]!);
            }
            port += Interlocked.Add(ref _nextBasePort, 1);

            TransportName = transport;
            IsSecure = secure;

            using var loggerFactory = LoggerFactory.Create(
                builder =>
                {
                    builder.AddSimpleConsole(configure => configure.IncludeScopes = true);
                    // builder.AddJsonConsole(configure =>
                    // {
                    //     configure.IncludeScopes = true;
                    //     configure.JsonWriterOptions = new System.Text.Json.JsonWriterOptions()
                    //     {
                    //         Indented = true
                    //     };
                    // });
                    builder.SetMinimumLevel(LogLevel.Trace);
                });

            _serverCommunicator = new Communicator(loggerFactory : loggerFactory);

            var serverOptions = new ServerOptions()
            {
                AcceptNonSecure = secure ? NonSecure.Never : NonSecure.Always,
                ColocationScope = transport == "colocated" ? ColocationScope.Communicator : ColocationScope.None,
                AuthenticationOptions = new SslServerAuthenticationOptions()
                {
                    ClientCertificateRequired = false,
                    ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
                }
            };
            serverOptionsBuilder?.Invoke(serverOptions);
            Server = new(_serverCommunicator, serverOptions);

            // TODO: support something like communicator/connection option builder
            _clientCommunicator = new Communicator(
                authenticationOptions: new SslClientAuthenticationOptions()
                {
                    RemoteCertificateValidationCallback = CertificateValidaton.GetServerCertificateValidationCallback(
                        certificateAuthorities: new X509Certificate2Collection()
                        {
                            new X509Certificate2("../../../certs/cacert.pem")
                        })
                },
                loggerFactory: loggerFactory);

            if (transport == "colocated")
            {
                ClientEndpoint = new ColocatedEndpoint(Server);
                ServerEndpoint = ClientEndpoint;
            }
            else
            {
                if (protocol == Protocol.Ice2)
                {
                    ServerEndpoint =
                        UriParser.ParseEndpoints($"ice+{transport}://127.0.0.1:{port}", _serverCommunicator)[0];
                    ClientEndpoint = IServicePrx.Parse($"{ServerEndpoint}/dummy", _clientCommunicator).Endpoints[0];
                }
                else
                {
                    ServerEndpoint =
                        Ice1Parser.ParseEndpoints($"{transport} -h 127.0.0.1 -p {port}", _serverCommunicator)[0];
                    ClientEndpoint = IServicePrx.Parse($"dummy:{ServerEndpoint}", _clientCommunicator).Endpoints[0];
                }

            }
        }

        [OneTimeTearDown]
        public async Task ShutdownAsync()
        {
            _acceptor?.Dispose();
            await _clientCommunicator.DisposeAsync();
            await Server.DisposeAsync();
            await _serverCommunicator.DisposeAsync();
        }

        protected IAcceptor CreateAcceptor() => ServerEndpoint.Acceptor(Server);

        protected async Task<MultiStreamSocket> ConnectAsync() => (await ConnectAndGetProxyAsync()).Socket;

        protected async Task<(MultiStreamSocket Socket, IServicePrx Proxy)> ConnectAndGetProxyAsync()
        {
            lock (_mutex)
            {
                _acceptor ??= CreateAcceptor();
            }

            NonSecure nonSecure = IsSecure ? NonSecure.Never : NonSecure.Always;
            Connection connection = await ClientEndpoint.ConnectAsync(nonSecure, null, default);
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
                Communicator = _clientCommunicator,
                Connection = connection,
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
