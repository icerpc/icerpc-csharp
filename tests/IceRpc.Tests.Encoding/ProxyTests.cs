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
                Communicator = _communicator
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
        public void Proxy_EncondingVersioning(byte encodingMajor, byte encodingMinor, string str)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            var ostr = new OutputStream(encoding, _data, startAt: default);

            var prx = IServicePrx.Parse(str, _communicator);
            ostr.WriteProxy(prx);
            ostr.Finish();

            var prx2 = _data[0].AsReadOnlyMemory().Read(encoding, IServicePrx.IceReader, source: prx);
            Assert.AreEqual(prx, prx2);
        }

        [TestCase(2, 0)]
        [TestCase(1, 1)]
        public void Proxy_Relative(byte encodingMajor, byte encodingMinor)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            // Create a relative proxy an clear is colocated connection.
            IServicePrx prx = _server.CreateRelativeProxy<IServicePrx>("/foo");
            var connection = prx.Connection;
            prx.Connection = null;

            // Marshal the relative proxy
            var ostr = new OutputStream(encoding, _data, startAt: default);
            ostr.WriteProxy(prx);
            ostr.Finish();

            // Unmarshals the relative proxy using a connection, we should get back a fixed
            // proxy tied to this connection.
            IServicePrx? prx1 = _data[0].AsReadOnlyMemory().Read(encoding,
                                                                IServicePrx.IceReader,
                                                                connection: connection,
                                                                proxyOptions: new ProxyOptions());
            Assert.That(connection == prx1.Connection, Is.True);
            CollectionAssert.IsEmpty(prx1.Endpoints);
            Assert.That(connection == prx1.Connection, Is.True);

            // Create a direct proxy and give it a connection
            var prx2 = IServicePrx.Parse("ice+tcp://localhost/bar", _communicator);
            prx2.Connection = connection;

            // Unmarshals the relative proxy using the direct proxy we just created, we should get back
            // a direct proxy that has the same connection and endpoints as the source proxy.
            prx1 = _data[0].AsReadOnlyMemory().Read(encoding, IServicePrx.IceReader, source: prx2);
            Assert.That(prx1.Connection, Is.Not.Null);
            CollectionAssert.IsNotEmpty(prx1.Endpoints);
            Assert.That(prx2.Connection == prx1.Connection, Is.True);
            Assert.That(prx2.Endpoints == prx1.Endpoints, Is.True);
        }
    }
}
