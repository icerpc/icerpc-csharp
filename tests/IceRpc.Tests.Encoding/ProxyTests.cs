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
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly Memory<byte> _buffer;

        public ProxyTests()
        {
            _buffer = new byte[256];
            _server = new Server
            {
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            _server.Listen();

            _connection = new Connection
            {
                RemoteEndpoint = _server.ProxyEndpoint,
                Options = OutgoingConnectionOptions.Default // TODO: it's required due to a bug in the Connection code
            };
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.ShutdownAsync();
            await _connection.ShutdownAsync();
        }

        [TestCase(2, 0, "ice+tcp://localhost:10000/foo?alt-endpoint=ice+tcp://localhost:10001")]
        [TestCase(1, 1, "ice+tcp://localhost:10000/foo?alt-endpoint=ice+tcp://localhost:10001")]
        [TestCase(2, 0, "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        [TestCase(1, 1, "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        public void Proxy_EncodingVersioning(byte encodingMajor, byte encodingMinor, string str)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            var ostr = new OutputStream(encoding, _buffer);

            var prx = IServicePrx.Parse(str);
            ostr.WriteProxy(prx);
            ReadOnlyMemory<byte> data = ostr.Finish().Span[0];

            var istr = new InputStream(data, encoding, connection: null, prx.Invoker);
            var prx2 = IServicePrx.IceReader(istr);
            istr.CheckEndOfBuffer(skipTaggedParams: false);
            Assert.AreEqual(prx, prx2);
        }

        [TestCase(2, 0)]
        [TestCase(1, 1)]
        public void Proxy_EndpointLess(byte encodingMajor, byte encodingMinor)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);

            // Create an endpointless proxy
            var endpointLess = IServicePrx.FromPath("/foo", _server.Protocol);

            var regular = IServicePrx.FromConnection(_connection, "/bar");

            // Marshal the endpointless proxy
            var ostr = new OutputStream(encoding, _buffer);
            ostr.WriteProxy(endpointLess);
            ReadOnlyMemory<byte> data = ostr.Finish().Span[0];

            // Unmarshals the endpointless proxy using the outgoing connection. We get back a 1-endpoint proxy
            var istr = new InputStream(data, encoding, _connection);
            var prx1 = IServicePrx.IceReader(istr);
            istr.CheckEndOfBuffer(skipTaggedParams: false);

            Assert.AreEqual(regular.Connection, prx1.Connection);
            Assert.AreEqual(prx1.Endpoint, regular.Connection!.RemoteEndpoint);
        }
    }
}
