// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture(ProtocolCode.Ice1)]
    [TestFixture(ProtocolCode.Ice2)]
    public sealed class OperationsTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly OperationsPrx _prx;
        private readonly DerivedOperationsPrx _derivedPrx;

        // TODO: in this Slice test, the protocol code is used to select the encoding of the payload. We should instead
        // use the ice2 protocol all the time and pass a parameter for the encoding.
        public OperationsTests(ProtocolCode protocol)
        {
            Endpoint serverEndpoint = TestHelper.GetUniqueColocEndpoint(Protocol.FromProtocolCode(protocol));
            _server = new Server
            {
                Dispatcher = new Operations(),
                Endpoint = serverEndpoint
            };
            _server.Listen();
            _connection = new Connection
            {
                RemoteEndpoint = serverEndpoint
            };
            _prx = OperationsPrx.FromConnection(_connection);
            _derivedPrx = new DerivedOperationsPrx(_prx.Proxy);

            Assert.AreEqual(protocol, _prx.Proxy.Protocol.Code);
        }

        [TearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Test]
        public async Task Operations_BuiltinTypesAsync()
        {
            await TestAsync((prx, p1, p2) => prx.OpByteAsync(p1, p2), byte.MinValue, byte.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpBoolAsync(p1, p2), false, true);
            await TestAsync((prx, p1, p2) => prx.OpShortAsync(p1, p2), short.MinValue, short.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpUShortAsync(p1, p2), ushort.MinValue, ushort.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpIntAsync(p1, p2), int.MinValue, int.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpVarIntAsync(p1, p2), int.MinValue, int.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpUIntAsync(p1, p2), uint.MinValue, uint.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpVarUIntAsync(p1, p2), uint.MinValue, uint.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpLongAsync(p1, p2), long.MinValue, long.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpVarLongAsync(p1, p2), int.MinValue, (long)int.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpULongAsync(p1, p2), ulong.MinValue, ulong.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpVarULongAsync(p1, p2), ulong.MinValue, uint.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpFloatAsync(p1, p2), float.MinValue, float.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpDoubleAsync(p1, p2), double.MinValue, double.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpStringAsync(p1, p2), "hello", "world");

            async Task TestAsync<T>(Func<IOperationsPrx, T, T, Task<(T, T)>> invoker, T p1, T p2)
            {
                foreach (IOperationsPrx prx in new IOperationsPrx[] { _prx, _derivedPrx })
                {
                    (T r1, T r2) = await invoker(prx, p1, p2);
                    Assert.AreEqual(p1, r1);
                    Assert.AreEqual(p2, r2);
                }
            }
        }

        [Test]
        public async Task Operations_OnewayAsync()
        {
            Assert.ThrowsAsync<SomeException>(async () => await _prx.OpOnewayAsync());
            await _prx.OpOnewayAsync(new Invocation { IsOneway = true });

            // This is invoked as a oneway thanks to the metadata
            await _prx.OpOnewayMetadataAsync();

            await _prx.IcePingAsync();
        }

        [TestCase("ice+tcp://host:1000/identity?foo=bar")]
        [TestCase("identity:tcp -h host -p 10000")]
        [TestCase("identity:opaque -t 99 -e 1.1 -v abcd")] // 99 = unknown and -t -e -v in this order
        [TestCase("identity:opaque -t 99 -e 1.0 -v CTEyNy4wLjAuMeouAAAQJwAAAA==")]
        [TestCase("identity:opaque -t 1 -e 1.1 -v CTEyNy4wLjAuMeouAAAQJwAAAA==",
                  "identity:tcp -h 127.0.0.1 -p 12010 -t 10000")]
        [TestCase("identity:opaque -t 1 -e 1.0 -v CTEyNy4wLjAuMeouAAAQJwAAAA==",
                  "identity:tcp -h 127.0.0.1 -p 12010 -t 10000")]
        public async Task Operations_ServiceAsync(string proxy, string? actualIce1Proxy = null)
        {
            var service = ServicePrx.Parse(proxy);
            ServicePrx result = await _prx.OpServiceAsync(service);

            if (_prx.Proxy.Protocol == Protocol.Ice1 && actualIce1Proxy != null)
            {
                Assert.AreEqual(ServicePrx.Parse(actualIce1Proxy), result);
            }
            else
            {
                Assert.AreEqual(service, result);
            }
        }

        [Test]
        public async Task Operations_OperationNotFoundExceptionAsync()
        {
            Endpoint endpoint = TestHelper.GetUniqueColocEndpoint();
            await using var server = new Server
            {
                Dispatcher = new NoOperations(),
                Endpoint = endpoint
            };
            server.Listen();
            await using var connection = new Connection
            {
                RemoteEndpoint = endpoint
            };
            var prx = OperationsPrx.FromConnection(connection);
            Assert.ThrowsAsync<OperationNotFoundException>(async () => await prx.OpBoolAsync(true, false));
        }

        public class Operations : Service, IOperations
        {
            // Builtin types
            public ValueTask<(byte, byte)> OpByteAsync(
                byte p1,
                byte p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(bool, bool)> OpBoolAsync(
                bool p1,
                bool p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(short, short)> OpShortAsync(
                short p1,
                short p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ushort, ushort)> OpUShortAsync(
                ushort p1,
                ushort p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(int, int)> OpIntAsync(
                int p1,
                int p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(int, int)> OpVarIntAsync(
                int p1,
                int p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(uint, uint)> OpUIntAsync(
                uint p1,
                uint p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(uint, uint)> OpVarUIntAsync(
                uint p1,
                uint p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(long, long)> OpLongAsync(
                long p1,
                long p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(long, long)> OpVarLongAsync(
                long p1,
                long p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ulong, ulong)> OpULongAsync(
                ulong p1,
                ulong p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ulong, ulong)> OpVarULongAsync(
                ulong p1,
                ulong p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(float, float)> OpFloatAsync(
                float p1,
                float p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(double, double)> OpDoubleAsync(
                double p1,
                double p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(string, string)> OpStringAsync(
                string p1,
                string p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<ServicePrx> OpServiceAsync(
                ServicePrx service,
                Dispatch dispatch,
                CancellationToken cancel) => new(service);

            // Oneway Operations

            public ValueTask OpOnewayAsync(Dispatch dispatch, CancellationToken cancel) => throw new SomeException();

            public ValueTask OpOnewayMetadataAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new SomeException();
        }
        public class NoOperations : Service, INoOperations
        {
        }
    }
}
