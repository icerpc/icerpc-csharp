// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    /// <summary>Test fixture for tests that need to test sockets. The constructor initialize a communicator and an
    /// Server and setup client/server endpoints for a configurable protocol/transport/security.</summary>
    public class SocketBaseTest
    {
        private protected SslClientAuthenticationOptions? ClientAuthenticationOptions =>
            IsSecure ? ClientConnectionOptions.AuthenticationOptions : null;
        private protected Endpoint ClientEndpoint { get; }
        private protected ILogger Logger { get; }
        protected IncomingConnectionOptions ServerConnectionOptions { get; }
        private protected bool IsIPv6 { get; }
        private protected bool IsSecure { get; }
        protected OutgoingConnectionOptions ClientConnectionOptions { get; }
        private protected SslServerAuthenticationOptions? ServerAuthenticationOptions =>
            IsSecure ? ServerConnectionOptions.AuthenticationOptions : null;
        private protected Endpoint ServerEndpoint { get; }
        private protected string TransportName { get; }

        private IAcceptor? _acceptor;
        private readonly AsyncSemaphore _acceptSemaphore = new(1);
        // Protects the _acceptor data member
        private readonly object _mutex = new();
        private static int _nextBasePort;

        public SocketBaseTest(
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
                port = int.Parse(TestContext.Parameters["IceRpc.Tests.Internal.BasePort"]!);
            }
            port += Interlocked.Add(ref _nextBasePort, 1);

            TransportName = transport;
            IsSecure = tls;
            IsIPv6 = addressFamily == AddressFamily.InterNetworkV6;

            ClientConnectionOptions = new OutgoingConnectionOptions()
            {
                AuthenticationOptions = new()
                {
                    RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection()
                                {
                                    new X509Certificate2("../../../certs/cacert.pem")
                                }),
                    TargetHost = IsIPv6 ? "[::1]" : "127.0.0.1"
                }
            };

            ServerConnectionOptions = new IncomingConnectionOptions()
            {
                AuthenticationOptions = new()
                {
                    ClientCertificateRequired = false,
                    ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
                }
            };

            Logger = Runtime.DefaultLoggerFactory.CreateLogger("IceRpc");

            if (transport == "coloc")
            {
                ClientEndpoint = new ColocEndpoint(Guid.NewGuid().ToString(), 4062, protocol);
                ServerEndpoint = ClientEndpoint;
            }
            else
            {
                if (protocol == Protocol.Ice2)
                {
                    string tlsOption = "";
                    if ((transport == "tcp" || transport == "ws") && !IsSecure)
                    {
                        tlsOption = "?tls=false";
                    }

                    string host = IsIPv6 ? "[::1]" : "127.0.0.1";
                    string endpoint = serverEndpoint?.Invoke(host, port) ??
                        $"ice+{transport}://{host}:{port}{tlsOption}";
                    ServerEndpoint = Endpoint.Parse(endpoint);
                    endpoint = clientEndpoint?.Invoke(host, port) ?? $"ice+{transport}://{host}:{port}{tlsOption}";
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
        public void Shutdown() => _acceptor?.Dispose();

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
                MultiStreamSocket multiStreamSocket = await _acceptor.AcceptAsync();
                Debug.Assert(multiStreamSocket.TransportName == TransportName);
                await multiStreamSocket.AcceptAsync(ServerAuthenticationOptions, default);
                if (ClientEndpoint.Protocol == Protocol.Ice2 && !multiStreamSocket.Socket.IsSecure)
                {
                    // If the accepted connection is not secured, we need to read the first byte from the socket.
                    // See above for the reason.
                    if (multiStreamSocket is MultiStreamOverSingleStreamSocket socket)
                    {
                        Memory<byte> buffer = new byte[1];
                        await socket.Underlying.ReceiveAsync(buffer, default);
                    }
                }
                return multiStreamSocket;
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

        protected async Task<MultiStreamSocket> ConnectAsync(OutgoingConnectionOptions? connectionOptions = null)
        {
            if (!ClientEndpoint.IsDatagram)
            {
                lock (_mutex)
                {
                    _acceptor ??= CreateAcceptor();
                }
            }

            MultiStreamSocket multiStreamSocket = ClientEndpoint.CreateClientSocket(
                connectionOptions ?? ClientConnectionOptions,
                Logger);
            await multiStreamSocket.ConnectAsync(ClientAuthenticationOptions, default);
            if (ClientEndpoint.Protocol == Protocol.Ice2 && !IsSecure)
            {
                // If establishing a non-secure Ice2 connection, we need to send a single byte. The peer peeks
                // a single byte over the socket to figure out if the client establishes a secure/non-secure
                // connection. If we were not providing this byte, the AcceptAsync from the peer would hang
                // indefinitely.
                if (multiStreamSocket is MultiStreamOverSingleStreamSocket socket)
                {
                    var buffer = new List<ArraySegment<byte>>() { new byte[1] { 0 } };
                    await socket.Underlying.SendAsync(buffer, default);
                }
            }

            if (multiStreamSocket.TransportName != TransportName)
            {
                Debug.Assert(TransportName == "coloc");
                Debug.Assert(multiStreamSocket is ColocSocket);
            }
            return multiStreamSocket;
        }

        protected IAcceptor CreateAcceptor() => ServerEndpoint.CreateAcceptor(ServerConnectionOptions, Logger);

        protected MultiStreamSocket CreateServerSocket() =>
            ServerEndpoint.CreateServerSocket(ServerConnectionOptions, Logger);
    }
}
