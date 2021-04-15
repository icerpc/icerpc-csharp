// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IceRpc.Tests.Encoding
{
    [Parallelizable(scope: ParallelScope.All)]
    public class ProxyTests
    {
        [TestCase(2, 0, "ice+tcp://localhost:10000/foo?alt-endpoint=ice+ws://localhost:10000")]
        [TestCase(1, 1, "ice+tcp://localhost:10000/foo?alt-endpoint=ice+ws://localhost:10000")]
        [TestCase(2, 0, "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        [TestCase(1, 1, "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        public async Task Proxy_Enconding(byte encodingMajor, byte encodingMinor, string str)
        {
            await using var communicator = new Communicator();
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            var data = new List<ArraySegment<byte>>() { new byte[256] };
            var ostr = new OutputStream(encoding, data, startAt: default, payloadEncoding: encoding, default);

            IServicePrx prx = IServicePrx.Parse(str, communicator);
            ostr.WriteProxy(prx);
            ostr.Finish();

            var istr = new InputStream(data[0], encoding, source: prx, startEncapsulation: true);
            var prx2 = IServicePrx.IceReader(istr);
            Assert.AreEqual(prx, prx2);
        }

        [TestCase(2, 0)]
        [TestCase(1, 1)]
        public async Task Proxy_Relative(byte encodingMajor, byte encodingMinor)
        {
            await using var communicator = new Communicator();
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            var data = new List<ArraySegment<byte>>() { new byte[256] };
            var ostr = new OutputStream(encoding, data, startAt: default, payloadEncoding: encoding, default);

            await using var server = new Server
            {
                Endpoint = "ice+tcp://[::1]:0",
                Communicator = communicator
            };
            _ = server.ListenAndServeAsync();
            IServicePrx prx1 = server.CreateRelativeProxy<IServicePrx>("/foo");
            IServicePrx prx2 = server.CreateProxy<IServicePrx>("/bar");
            var connection = await prx2.GetConnectionAsync();
            ostr.WriteProxy(prx1);
            ostr.Finish();

            var istr = new InputStream(
                data[0],
                encoding,
                connection: connection,
                proxyOptions: new ProxyOptions(),
                startEncapsulation: true);
            prx1 = IServicePrx.IceReader(istr);
            // Reference equality
            Assert.That(connection == prx1.Connection, Is.True);
            CollectionAssert.IsEmpty(prx1.Endpoints);

            prx1 = server.CreateRelativeProxy<IServicePrx>("/foo");
            ostr = new OutputStream(encoding, data, startAt: default, payloadEncoding: encoding, default);
            ostr.WriteProxy(prx1);
            ostr.Finish();

            istr = new InputStream(
                data[0],
                encoding,
                source: prx2,
                startEncapsulation: true);
            prx1 = IServicePrx.IceReader(istr);
            // Reference equality
            Assert.IsNotNull(prx1.Connection);
            CollectionAssert.IsNotEmpty(prx1.Endpoints);
            Assert.That(prx2.Connection == prx1.Connection, Is.True);
            Assert.That(prx2.Endpoints == prx1.Endpoints, Is.True);
        }
    }
}
