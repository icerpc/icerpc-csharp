// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Ice;
using ZeroC.Test;

namespace ZeroC.IceSSL.Test.Configuration
{
    internal sealed class ServerService : IServer
    {
        private readonly Server _server;
        private readonly Communicator _communicator;

        internal ServerService(Communicator communicator, Server server)
        {
            _communicator = communicator;
            _server = server;
        }

        public void NoCert(Current current, CancellationToken cancel)
        {
            try
            {
                var tcpConnection = (TcpConnection)current.Connection;
                TestHelper.Assert(tcpConnection.IsEncrypted);
                TestHelper.Assert(tcpConnection.RemoteCertificate == null);
            }
            catch
            {
                TestHelper.Assert(false);
            }
        }

        public void CheckCert(string subjectDN, string issuerDN, Current current, CancellationToken cancel)
        {
            try
            {
                var tcpConnection = (TcpConnection)current.Connection;
                var ice1 = current.Server.Protocol == Protocol.Ice1;
                TestHelper.Assert(tcpConnection.IsEncrypted);
                TestHelper.Assert(tcpConnection.RemoteCertificate != null);
                TestHelper.Assert(tcpConnection.RemoteCertificate.Subject.Equals(subjectDN));
                TestHelper.Assert(tcpConnection.RemoteCertificate.Issuer.Equals(issuerDN));
            }
            catch
            {
                TestHelper.Assert(false);
            }
        }

        public void CheckCipher(string cipher, Current current, CancellationToken cancel)
        {
            try
            {
                var tcpConnection = (TcpConnection)current.Connection;
                var ice1 = current.Server.Protocol == Protocol.Ice1;
                TestHelper.Assert(tcpConnection.IsEncrypted);
                TestHelper.Assert(tcpConnection.NegotiatedCipherSuite!.ToString()!.Equals(cipher));
            }
            catch
            {
                TestHelper.Assert(false);
            }
        }

        internal Task ShutdownAsync() =>
            Task.WhenAll(_server.ShutdownAsync(), _communicator.ShutdownAsync());
    }

    internal sealed class ServerFactory : IAsyncServerFactory
    {
        private readonly string _defaultDir;
        private readonly Dictionary<Identity, ServerService> _servers = new();

        public ServerFactory(string defaultDir) => _defaultDir = defaultDir;

        public async ValueTask<IServerPrx> CreateServerAsync(
            Dictionary<string, string> properties,
            bool requireClientCertificate,
            Current current,
            CancellationToken cancel)
        {
            properties["IceSSL.DefaultDir"] = _defaultDir;
            var communicator = new Communicator(
                properties,
                tlsServerOptions: new TlsServerOptions()
                {
                    RequireClientCertificate = requireClientCertificate
                });

            bool ice1 = TestHelper.GetTestProtocol(communicator.GetProperties()) == Protocol.Ice1;
            string host = TestHelper.GetTestHost(communicator.GetProperties());

            string serverEndpoint = TestHelper.GetTestEndpoint(
                properties: communicator.GetProperties(),
                num: 1,
                transport: ice1 ? "ssl" : "tcp",
                ephemeral: host != "localhost");

            var server = new Server(
                communicator,
                new()
                {
                    ColocationScope = ColocationScope.Communicator,
                    Endpoints = serverEndpoint,
                    PublishedHost = host
                });
            var serverService = new ServerService(communicator, server);
            IServerPrx prx = server.AddWithUUID(serverService, IServerPrx.Factory);
            _servers[prx.Identity] = serverService;
            await server.ActivateAsync(cancel);
            return prx;
        }

        public async ValueTask DestroyServerAsync(IServerPrx? srv, Current current, CancellationToken cancel)
        {
            if (_servers.TryGetValue(srv!.Identity, out ServerService? server))
            {
                await server.ShutdownAsync();
                _servers.Remove(srv.Identity);
            }
        }

        public ValueTask ShutdownAsync(Current current, CancellationToken cancel)
        {
            TestHelper.Assert(_servers.Count == 0);
            _ = current.Server.ShutdownAsync();
            return default;
        }
    }
}
