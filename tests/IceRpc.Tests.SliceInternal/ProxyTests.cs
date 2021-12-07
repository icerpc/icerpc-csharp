// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.SliceInternal
{
    [Parallelizable(ParallelScope.All)]
    public sealed class ProxyTests
    {
        [TestCase("2.0", "ice+tcp://localhost:10000/foo?alt-endpoint=ice+tcp://localhost:10001")]
        [TestCase("1.1", "ice+tcp://localhost:10000/foo?alt-endpoint=ice+tcp://localhost:10001")]
        [TestCase("2.0", "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        [TestCase("1.1", "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        public async Task Proxy_EncodingVersioning(string encodingStr, string str)
        {
            var encoding = IceEncoding.FromString(encodingStr);
            IceEncoder encoder = encoding.CreateIceEncoder(new BufferWriter(new byte[256]));

            var proxy = Proxy.Parse(str);
            encoder.EncodeProxy(proxy);
            ReadOnlyMemory<byte> data = encoder.BufferWriter.Finish().Span[0];

            await using var connection = new Connection();

            IceDecoder decoder = encoding == Encoding.Ice11 ? new Ice11Decoder(data, connection) :
                new Ice20Decoder(data, connection);
            Proxy proxy2 = decoder.DecodeProxy();
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            Assert.AreEqual(proxy, proxy2);
        }

        [TestCase("2.0")]
        [TestCase("1.1")]
        public async Task Proxy_EndpointLess(string encodingStr)
        {
            await using ServiceProvider serviceProvider = new IntegrationServiceCollection().BuildServiceProvider();
            Connection connection = serviceProvider.GetRequiredService<Connection>();

            var encoding = IceEncoding.FromString(encodingStr);

            // Create an endpointless proxy
            var endpointLess = Proxy.FromPath("/foo", connection.Protocol);

            var regular = Proxy.FromConnection(connection, "/bar");

            // Encodes the endpointless proxy
            IceEncoder encoder = encoding.CreateIceEncoder(new BufferWriter(new byte[256]));
            encoder.EncodeProxy(endpointLess);
            ReadOnlyMemory<byte> data = encoder.BufferWriter.Finish().Span[0];

            // Decodes the endpointless proxy using the client connection. We get back a 1-endpoint proxy
            IceDecoder decoder = encoding == IceRpc.Encoding.Ice11 ?
                new Ice11Decoder(data, connection) :
                new Ice20Decoder(data, connection);

            Proxy proxy1 = decoder.DecodeProxy();
            decoder.CheckEndOfBuffer(skipTaggedParams: false);

            Assert.AreEqual(regular.Connection, proxy1.Connection);
            Assert.AreEqual(proxy1.Endpoint, regular.Connection!.RemoteEndpoint);
        }
    }
}
