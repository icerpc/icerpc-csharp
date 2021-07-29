// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    public class CustomClientTransport : IClientTransport
    {
        private IClientTransport _transport = new TcpClientTransport();
        public MultiStreamConnection CreateConnection(
            Endpoint remoteEndpoint, 
            ClientConnectionOptions options,
            ILogger logger) => _transport.CreateConnection(remoteEndpoint, options, logger);
    }

    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    class CustomTransportTests
    {

        [Test]
        public async Task Compress_Payload()
        {
        }
    }
}
