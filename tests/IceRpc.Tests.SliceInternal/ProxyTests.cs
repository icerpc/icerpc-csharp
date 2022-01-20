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
        [TestCase("2.0", "icerpc://localhost:10000/foo?alt-endpoint=localhost:10001")]
        [TestCase("1.1", "icerpc://localhost:10000/foo?alt-endpoint=localhost:10001")]
        [TestCase("2.0", "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        [TestCase("1.1", "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        public async Task Proxy_EncodingVersioning(string encodingStr, string str)
        {
            Memory<byte> buffer = new byte[256];
            var bufferWriter = new SingleBufferWriter(buffer);
            var encoding = IceEncoding.FromString(encodingStr);

            IProxyFormat? format = str.StartsWith("ice", StringComparison.Ordinal) ? null : IceProxyFormat.Default;
            var proxy = Proxy.Parse(str, format: format);
            EncodeProxy();

            buffer = bufferWriter.WrittenBuffer;

            await using var connection = new Connection();

            Proxy proxy2 = DecodeProxy();
            Assert.AreEqual(proxy, proxy2);

            void EncodeProxy()
            {
                var encoder = new IceEncoder(bufferWriter, encoding);
                encoder.EncodeProxy(proxy);
            }

            Proxy DecodeProxy()
            {
                var decoder = new IceDecoder(buffer, encoding, connection);
                Proxy p = decoder.DecodeProxy();
                decoder.CheckEndOfBuffer(skipTaggedParams: false);
                return p;
            }
        }

        [Test]
        public async Task Proxy_Relative()
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection().BuildServiceProvider();
            Connection connection = serviceProvider.GetRequiredService<Connection>();

            // Create a relative proxy
            var endpointLess = Proxy.FromPath("/foo");

            var regular = Proxy.FromConnection(connection, "/bar");

            Memory<byte> buffer = new byte[256];
            var bufferWriter = new SingleBufferWriter(buffer);
            EncodeProxy();
            buffer = bufferWriter.WrittenBuffer;

            Proxy proxy1 = DecodeProxy();

            Assert.AreEqual(regular.Connection, proxy1.Connection);
            Assert.AreEqual(proxy1.Endpoint, regular.Connection!.RemoteEndpoint);

            void EncodeProxy()
            {
                // Encodes the relative proxy
                var encoder = new IceEncoder(bufferWriter, Encoding.Slice20);
                encoder.EncodeProxy(endpointLess);
            }

            Proxy DecodeProxy()
            {
                // Decodes the relative proxy using the client connection. We get back a 1-endpoint proxy
                var decoder = new IceDecoder(buffer, Encoding.Slice20, connection);

                Proxy p = decoder.DecodeProxy();
                decoder.CheckEndOfBuffer(skipTaggedParams: false);
                return p;
            }
        }
    }
}
