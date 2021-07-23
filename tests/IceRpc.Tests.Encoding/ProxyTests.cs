// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace IceRpc.Tests.Encoding
{
    [Parallelizable(scope: ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public sealed class ProxyTests : IAsyncDisposable
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
                RemoteEndpoint = _server.Endpoint,
                Options = ClientConnectionOptions.Default // TODO: it's required due to a bug in the Connection code
            };
        }

        [TearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [TestCase(2, 0, "ice+tcp://localhost:10000/foo?alt-endpoint=ice+tcp://localhost:10001")]
        [TestCase(1, 1, "ice+tcp://localhost:10000/foo?alt-endpoint=ice+tcp://localhost:10001")]
        [TestCase(2, 0, "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        [TestCase(1, 1, "foo -f facet:tcp -h localhost -p 10000:udp -h localhost -p 10000")]
        public async Task Proxy_EncodingVersioning(byte encodingMajor, byte encodingMinor, string str)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);
            var encoder = new IceEncoder(encoding, _buffer);

            var proxy = Proxy.Parse(str);
            encoder.EncodeProxy(proxy);
            ReadOnlyMemory<byte> data = encoder.Finish().Span[0];

            await using var connection = new Connection();

            var decoder = new IceDecoder(data, encoding, connection, invoker: null);
            var proxy2 = decoder.DecodeProxy();
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            Assert.AreEqual(proxy, proxy2);
        }

        [TestCase(2, 0)]
        [TestCase(1, 1)]
        public void Proxy_EndpointLess(byte encodingMajor, byte encodingMinor)
        {
            var encoding = new IceRpc.Encoding(encodingMajor, encodingMinor);

            // Create an endpointless proxy
            var endpointLess = Proxy.FromPath("/foo", _server.Protocol);

            var regular = Proxy.FromConnection(_connection, "/bar");

            // Marshal the endpointless proxy
            var encoder = new IceEncoder(encoding, _buffer);
            encoder.EncodeProxy(endpointLess);
            ReadOnlyMemory<byte> data = encoder.Finish().Span[0];

            // Unmarshals the endpointless proxy using the client connection. We get back a 1-endpoint proxy
            var decoder = new IceDecoder(data, encoding, _connection);
            var proxy1 = decoder.DecodeProxy();
            decoder.CheckEndOfBuffer(skipTaggedParams: false);

            Assert.AreEqual(regular.Connection, proxy1.Connection);
            Assert.AreEqual(proxy1.Endpoint, regular.Connection!.RemoteEndpoint);
        }
    }
}
