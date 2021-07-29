// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    public class CustomClientTransport : IClientTransport
    {
        private readonly IClientTransport _transport = new TcpClientTransport();

        public MultiStreamConnection CreateConnection(
            Endpoint remoteEndpoint,
            ClientConnectionOptions connectionOptions,
            ILoggerFactory loggerFactory) =>
            _transport.CreateConnection(remoteEndpoint, connectionOptions, loggerFactory);
    }

    public class CustomServerTransport : IServerTransport
    {
        private readonly IServerTransport _transport = new TcpServerTransport();

        public (IListener? Listener, MultiStreamConnection? Connection) Listen(
            Endpoint endpoint,
            ServerConnectionOptions connectionOptions,
            ILoggerFactory loggerFactory) => _transport.Listen(endpoint, connectionOptions, loggerFactory);
    }

    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    public class CustomTransportTests
    {
        [Test]
        public async Task CustomTransport_IcePingAsync()
        {
            await using var server = new Server
            {
                ServerTransport = new CustomServerTransport(),
                Endpoint = "ice+custom://127.0.0.1:0?tls=false",
                Dispatcher = new MyService()
            };

            server.Listen();

            await using var connection = new Connection
            {
                ClientTransport = new CustomClientTransport(),
                RemoteEndpoint = server.Endpoint
            };

            var prx = ServicePrx.FromConnection(connection);
            await prx.IcePingAsync();
        }

        public class MyService : Service, IService
        {
        }
    }
}
