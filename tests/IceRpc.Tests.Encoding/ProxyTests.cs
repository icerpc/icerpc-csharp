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
        [TestCase(2, 0, "foo:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        [TestCase(1, 1, "foo:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
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
    }
}
