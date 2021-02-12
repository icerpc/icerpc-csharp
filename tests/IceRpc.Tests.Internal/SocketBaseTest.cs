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
    // ObjectAdapter and establish a connection with a configurable transport.<summary>
    public class SocketBaseTest
    {
        private protected bool IsSecure => _secure;
        private protected string TransportName => _transport;

        private IAcceptor? _acceptor;
        private readonly ObjectAdapter _adapter;
        private readonly Communicator _clientCommunicator;
        private readonly Endpoint _clientEndpoint;
        private static int _nextBasePort;
        private readonly bool _secure;
        private readonly Communicator _serverCommunicator;
        private readonly Endpoint _serverEndpoint;
        private readonly string _transport;

        public SocketBaseTest(Protocol protocol, string transport, bool secure)
        {
            int port = 12000;
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.Internal.BasePort"))
            {
                port = int.Parse(TestContext.Parameters["IceRpc.Tests.Internal.BasePort"]!);
            }
            port += Interlocked.Add(ref _nextBasePort, 1);

            _transport = transport;
            _secure = secure;

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
            string endpoint = protocol == Protocol.Ice2 ?
                $"ice+{endpointTransport}://localhost:{port}" : $"{endpointTransport} -h localhost -p {port}";

            _adapter = new ObjectAdapter(
                _serverCommunicator,
                "TestAdapter-0",
                new ObjectAdapterOptions()
                {
                    AcceptNonSecure = secure ? NonSecure.Never : NonSecure.Always,
                    ColocationScope = transport == "colocated" ? ColocationScope.Communicator : ColocationScope.None,
                    Endpoints = endpoint
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
            _clientEndpoint = IObjectPrx.Parse(proxy.ToString()!, _clientCommunicator).Endpoints[0];
            _serverEndpoint = IObjectPrx.Parse(proxy.ToString()!, _serverCommunicator).Endpoints[0];
        }

        [OneTimeSetUp]
        public async Task InitializeAsync()
        {
            Endpoint serverEndpoint = (await _serverEndpoint.ExpandHostAsync(default)).First();
            _acceptor = serverEndpoint.Acceptor(_adapter);
        }

        [OneTimeTearDown]
        public async Task ShutdownAsync()
        {
            _acceptor!.Dispose();
            await _clientCommunicator.DisposeAsync();
            await _adapter.DisposeAsync();
            await _serverCommunicator.DisposeAsync();
        }

        protected async Task<MultiStreamSocket> ConnectAsync()
        {
            NonSecure nonSecure = _secure ? NonSecure.Never : NonSecure.Always;
            Connection connection = await _clientEndpoint.ConnectAsync(nonSecure, null, default);
            Debug.Assert(connection.Endpoint.TransportName == _transport);
            return connection.Socket;
        }

        protected async Task<MultiStreamSocket> AcceptAsync()
        {
            Connection connection = await _acceptor!.AcceptAsync();
            Debug.Assert(connection.Endpoint.TransportName == _transport);
            return connection.Socket;
        }
    }
}
