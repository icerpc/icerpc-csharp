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
        [TestCase(SliceEncoding.Slice2, "icerpc://localhost:10000/foo?alt-endpoint=localhost:10001")]
        [TestCase(SliceEncoding.Slice1, "icerpc://localhost:10000/foo?alt-endpoint=localhost:10001")]
        [TestCase(SliceEncoding.Slice2, "foo -f facet:tcp -h localhost -p 10000:tcp -h localhost -p 20000")]
        [TestCase(SliceEncoding.Slice1, "foo -f facet:tcp -h localhost -p 10000:tcp -h localhost -p 20000")]
        public async Task Proxy_EncodingVersioning(SliceEncoding encoding, string str)
        {
            Memory<byte> buffer = new byte[256];
            var bufferWriter = new MemoryBufferWriter(buffer);

            IProxyFormat? format = str.StartsWith("ice", StringComparison.Ordinal) ? null : IceProxyFormat.Default;
            var proxy = Proxy.Parse(str, format: format);
            EncodeProxy();

            buffer = bufferWriter.WrittenMemory;

            await using var connection = new Connection("icerpc://[::1]");

            Proxy proxy2 = DecodeProxy();
            Assert.That(proxy, Is.EqualTo(proxy2));

            void EncodeProxy()
            {
                var encoder = new SliceEncoder(bufferWriter, encoding);
                encoder.EncodeProxy(proxy);
            }

            Proxy DecodeProxy()
            {
                var decoder = new SliceDecoder(buffer, encoding, connection);
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
            var bufferWriter = new MemoryBufferWriter(buffer);
            EncodeProxy();
            buffer = bufferWriter.WrittenMemory;

            Proxy proxy1 = DecodeProxy();

            Assert.That(regular.Connection, Is.EqualTo(proxy1.Connection));

            void EncodeProxy()
            {
                // Encodes the relative proxy
                var encoder = new SliceEncoder(bufferWriter, SliceEncoding.Slice2);
                encoder.EncodeProxy(endpointLess);
            }

            Proxy DecodeProxy()
            {
                // Decodes the relative proxy using the client connection. We get back a 1-endpoint proxy
                var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2, connection);

                Proxy p = decoder.DecodeProxy();
                decoder.CheckEndOfBuffer(skipTaggedParams: false);
                return p;
            }
        }
    }
}
