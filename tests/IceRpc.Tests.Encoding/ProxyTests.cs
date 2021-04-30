// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IceRpc.Tests.Encoding
{
    [Parallelizable(scope: ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class ProxyTests
    {
        private Communicator _communicator;
        private Server _server;
        private List<ArraySegment<byte>> _data;

        public ProxyTests()
        {
            _communicator = new Communicator();
            _data = new List<ArraySegment<byte>>() { new byte[256] };
            _server = new Server
            {
                Invoker = _communicator,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            _server.Listen();
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.ShutdownAsync();
            await _communicator.ShutdownAsync();
        }

        [TestCase(2, 0, "ice+tcp://localhost:10000/foo?alt-endpoint=ice+ws://localhost:10000")]
        [TestCase(1, 1, "ice+tcp://localhost:10000/foo?alt-endpoint=ice+ws://localhost:10000")]
        [TestCase(2, 0, "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        [TestCase(1, 1, "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        public void Proxy_EncodingVersioning(byte encodingMajor, byte encodingMinor, string str)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            var ostr = new OutputStream(encoding, _data, startAt: default);

            var prx = IServicePrx.Parse(str, _communicator);
            ostr.WriteProxy(prx);
            ostr.Finish();

            var prx2 = _data[0].AsReadOnlyMemory().Read(encoding,
                                                        IServicePrx.IceReader,
                                                        connection: null,
                                                        prx.GetOptions());
            Assert.AreEqual(prx, prx2);
        }

        [TestCase(2, 0)]
        [TestCase(1, 1)]
        public async Task Proxy_EndpointLessAsync(byte encodingMajor, byte encodingMinor)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);

            // Create an endpointless proxy
            IServicePrx endpointLess = _server.CreateEndpointlessProxy<IServicePrx>("/foo");

            IServicePrx regular = _server.CreateProxy<IServicePrx>("/bar");
            Connection connection = await regular.GetConnectionAsync();

            // Marshal the endpointless proxy
            var ostr = new OutputStream(encoding, _data, startAt: default);
            ostr.WriteProxy(endpointLess);
            ostr.Finish();

            // Unmarshals the endpointless proxy using the outgoing connection. We get back a 1-endpoint proxy
            IServicePrx prx1 = _data[0].AsReadOnlyMemory().Read(encoding,
                                                                IServicePrx.IceReader,
                                                                connection,
                                                                proxyOptions: new ProxyOptions());
            Assert.AreEqual(connection, prx1.Connection);
            Assert.AreEqual(prx1.Endpoint, connection.RemoteEndpoint.ToString());
        }
    }
}
