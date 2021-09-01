// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Encoding
{
    [Parallelizable(scope: ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public sealed class ProxyTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly BufferWriter _bufferWriter;

        public ProxyTests()
        {
            _bufferWriter = new BufferWriter(new byte[256]);
            _server = new Server
            {
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            _server.Listen();

            _connection = new Connection { RemoteEndpoint = _server.Endpoint };
        }

        [TearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [TestCase("2.0", "ice+tcp://localhost:10000/foo?alt-endpoint=ice+tcp://localhost:10001")]
        [TestCase("1.1", "ice+tcp://localhost:10000/foo?alt-endpoint=ice+tcp://localhost:10001")]
        [TestCase("2.0", "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        [TestCase("1.1", "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        public async Task Proxy_EncodingVersioning(string encodingStr, string str)
        {
            var encoding = IceEncoding.FromString(encodingStr);
            var encoder = encoding.CreateIceEncoder(_bufferWriter);

            var proxy = Proxy.Parse(str);
            encoder.EncodeProxy(proxy);
            ReadOnlyMemory<byte> data = _bufferWriter.Finish().Span[0];

            await using var connection = new Connection();

            IceDecoder decoder = encoding == IceRpc.Encoding.Ice11 ? new Ice11Decoder(data, connection) :
                new Ice20Decoder(data, connection);
            var proxy2 = decoder.DecodeProxy();
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            Assert.AreEqual(proxy, proxy2);
        }

        [TestCase("2.0")]
        [TestCase("1.1")]
        public void Proxy_EndpointLess(string encodingStr)
        {
            var encoding = IceEncoding.FromString(encodingStr);

            // Create an endpointless proxy
            var endpointLess = Proxy.FromPath("/foo", _server.Protocol);

            var regular = Proxy.FromConnection(_connection, "/bar");

            // Encodes the endpointless proxy
            var encoder = encoding.CreateIceEncoder(_bufferWriter);
            encoder.EncodeProxy(endpointLess);
            ReadOnlyMemory<byte> data = _bufferWriter.Finish().Span[0];

            // Decodes the endpointless proxy using the client connection. We get back a 1-endpoint proxy
            IceDecoder decoder = encoding == IceRpc.Encoding.Ice11 ? new Ice11Decoder(data, _connection) :
                new Ice20Decoder(data, _connection);

            var proxy1 = decoder.DecodeProxy();
            decoder.CheckEndOfBuffer(skipTaggedParams: false);

            Assert.AreEqual(regular.Connection, proxy1.Connection);
            Assert.AreEqual(proxy1.Endpoint, regular.Connection!.RemoteEndpoint);
        }
    }
}
