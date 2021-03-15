// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private protected Endpoint ClientEndpoint { get; }
        private protected Endpoint ServerEndpoint { get; }

        private protected bool IsSecure { get; }
        private protected string TransportName { get; }

        private IAcceptor? _acceptor;
        private AsyncSemaphore _acceptSemaphore = new(1);
        private readonly Server _server;
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
                    builder.SetMinimumLevel(LogLevel.Debug);
                });

            _serverCommunicator = new Communicator(loggerFactory : loggerFactory);

            string endpointTransport = transport == "colocated" ? "tcp" : transport;

            // It's important to use "localhost" here and not an IP address since the server will otherwise
            // create the acceptor in its constructor instead of its ActivateAsync method.
            string endpoint = protocol == Protocol.Ice2 ?
                $"ice+{endpointTransport}://localhost:{port}" : $"{endpointTransport} -h localhost -p {port}";

            var serverOptions = new ServerOptions()
            {
                AcceptNonSecure = secure ? NonSecure.Never : NonSecure.Always,
                ColocationScope = transport == "colocated" ? ColocationScope.Communicator : ColocationScope.None,
                Endpoints = endpoint,
                AuthenticationOptions = new SslServerAuthenticationOptions()
                {
                    ClientCertificateRequired = false,
                    ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
                }
            };
            serverOptionsBuilder?.Invoke(serverOptions);
            _server = new(_serverCommunicator, serverOptions);

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
                ClientEndpoint = new ColocatedEndpoint(_server);
                ServerEndpoint = ClientEndpoint;
            }
            else
            {
                var proxy = IServicePrx.Factory.Create(_server, "dummy");
                ClientEndpoint = IServicePrx.Parse(proxy.ToString()!, _clientCommunicator).Endpoints[0];
                ServerEndpoint = IServicePrx.Parse(proxy.ToString()!, _serverCommunicator).Endpoints[0];
            }
        }

        [OneTimeTearDown]
        public async Task ShutdownAsync()
        {
            _acceptor?.Dispose();
            await _clientCommunicator.DisposeAsync();
            await _server.DisposeAsync();
            await _serverCommunicator.DisposeAsync();
        }

        protected async ValueTask<IAcceptor> CreateAcceptorAsync()
        {
            Endpoint serverEndpoint;
            if (TransportName == "colocated")
            {
                serverEndpoint = ServerEndpoint;
            }
            else
            {
                serverEndpoint = (await ServerEndpoint.ExpandHostAsync(default)).First();
            }
            return serverEndpoint.Acceptor(_server);
        }

        protected async Task<MultiStreamSocket> ConnectAsync() => (await ConnectAndGetProxyAsync()).Socket;

        protected async Task<(MultiStreamSocket Socket, IServicePrx Proxy)> ConnectAndGetProxyAsync()
        {
            lock (_mutex)
            {
                _acceptor ??= CreateAcceptorAsync().AsTask().Result;
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
                _acceptor ??= CreateAcceptorAsync().AsTask().Result;
            }

            await _acceptSemaphore.EnterAsync();
            try
            {
                Connection connection = await _acceptor.AcceptAsync();
                Debug.Assert(connection.Endpoint.TransportName == TransportName);
                await connection.Socket.AcceptAsync(default);
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
