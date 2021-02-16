// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Ice;

namespace IceRpc.Tests.Internal
{
    /// <summary>Test fixture for tests that need to test sockets. The constructor initialize a communicator and an
    // ObjectAdapter and setup client/server endpoints for a configurable protocol/transport/security.<summary>
    public class SocketBaseTest
    {
        private protected Endpoint ClientEndpoint { get; }
        private protected Endpoint ServerEndpoint { get; }

        private protected bool IsSecure { get; }
        private protected string TransportName { get; }

        private IAcceptor? _acceptor;
        private readonly ObjectAdapter _adapter;
        private readonly Communicator _clientCommunicator;

        // Protects the _acceptor data member
        private readonly object _mutex = new();
        private static int _nextBasePort;
        private readonly Communicator _serverCommunicator;

        public SocketBaseTest(Protocol protocol, string transport, bool secure)
        {
            int port = 12000;
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.Internal.BasePort"))
            {
                port = int.Parse(TestContext.Parameters["IceRpc.Tests.Internal.BasePort"]!);
            }
            port += Interlocked.Add(ref _nextBasePort, 1);

            TransportName = transport;
            IsSecure = secure;

            _serverCommunicator = new Communicator(
                new Dictionary<string, string>
                {
                    { "IceSSL.DefaultDir", "../../../certs" },
                    { "IceSSL.CertFile", "server.p12" },
                    { "IceSSL.Password", "password" },
                    { "IceSSL.Keychain", "test.keychain" },
                    { "IceSSL.KeychainPassword", "password" },
                    // { "Ice.Trace.Transport", "3" },
                    // { "IceSSL.Trace.Security", "2" },
                },
                tlsServerOptions: new TlsServerOptions() {
                    RequireClientCertificate = false
                });

            string endpointTransport = transport == "colocated" ? "tcp" : transport;

            // It's important to use "localhost" here and not an IP address since the object adapter will otherwise
            // create the acceptor in its constructor instead of its ActivateAsync method.
            string endpoint = protocol == Protocol.Ice2 ?
                $"ice+{endpointTransport}://localhost:{port}" : $"{endpointTransport} -h localhost -p {port}";

            _adapter = new(
                _serverCommunicator,
                new()
                {
                    AcceptNonSecure = secure ? NonSecure.Never : NonSecure.Always,
                    ColocationScope = transport == "colocated" ? ColocationScope.Communicator : ColocationScope.None,
                    Endpoints = endpoint,
                });

            _clientCommunicator = new Communicator(
                new Dictionary<string, string>
                {
                    { "Ice.Default.PreferNonSecure", secure ? "Never" : "Always" },
                    { "IceSSL.DefaultDir", "../../../certs" },
                    { "IceSSL.CAs", "cacert.pem" },
                    // { "Ice.Trace.Transport", "3" },
                    // { "IceSSL.Trace.Security", "2" },
                });

            var proxy = _adapter.CreateProxy("dummy", IObjectPrx.Factory);
            ClientEndpoint = IObjectPrx.Parse(proxy.ToString()!, _clientCommunicator).Endpoints[0];
            ServerEndpoint = IObjectPrx.Parse(proxy.ToString()!, _serverCommunicator).Endpoints[0];
        }

        [OneTimeTearDown]
        public async Task ShutdownAsync()
        {
            _acceptor?.Dispose();
            await _clientCommunicator.DisposeAsync();
            await _adapter.DisposeAsync();
            await _serverCommunicator.DisposeAsync();
        }

        protected async ValueTask<IAcceptor> CreateAcceptorAsync()
        {
            Endpoint serverEndpoint = (await ServerEndpoint.ExpandHostAsync(default)).First();
            return serverEndpoint.Acceptor(_adapter);
        }

        protected async Task<MultiStreamSocket> ConnectAsync()
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
                SingleStreamSocket socket = (connection.Socket as MultiStreamOverSingleStreamSocket)!.Underlying;
                var buffer = new List<ArraySegment<byte>>() { new byte[1] { 0 } };
                await socket.SendAsync(buffer, default);
            }
            Debug.Assert(connection.Endpoint.TransportName == TransportName);
            return connection.Socket;
        }

        protected async Task<MultiStreamSocket> AcceptAsync()
        {
            lock (_mutex)
            {
                _acceptor ??= CreateAcceptorAsync().AsTask().Result;
            }

            Connection connection = await _acceptor.AcceptAsync();
            Debug.Assert(connection.Endpoint.TransportName == TransportName);
            await connection.Socket.AcceptAsync(default);
            if (ClientEndpoint.Protocol == Protocol.Ice2 && !connection.IsSecure)
            {
                // If the accepted connection is not secured, we need to read the first byte from the socket.
                // See above for the reason.
                Memory<byte> buffer = new byte[1];
                SingleStreamSocket socket = (connection.Socket as MultiStreamOverSingleStreamSocket)!.Underlying;
                await socket.ReceiveAsync(buffer, default);
            }
            return connection.Socket;
        }
    }
}
