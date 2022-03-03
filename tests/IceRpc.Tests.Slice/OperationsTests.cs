// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("ice")]
    [TestFixture("icerpc")]
    public sealed class OperationsTests : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly OperationsPrx _prx;
        private readonly DerivedOperationsPrx _derivedPrx;

        public OperationsTests(string protocol)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher, Operations>()
                .BuildServiceProvider();

            _prx = OperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
            _derivedPrx = new DerivedOperationsPrx(_prx.Proxy);
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        [Log(LogAttributeLevel.Debug)]
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
                    Assert.That(r1, Is.EqualTo(p1));
                    Assert.That(r2, Is.EqualTo(p2));
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

            await new ServicePrx(_prx.Proxy).IcePingAsync();
        }

        [TestCase("icerpc://host:1000/identity?foo=bar")]
        [TestCase("identity:tcp -h host -p 10000")]
        [TestCase("identity:opaque -t 99 -e 1.1 -v abcd")] // 99 = unknown and -t -e -v in this order
        [TestCase("identity:opaque -t 99 -e 1.0 -v CTEyNy4wLjAuMeouAAAQJwAAAA==")]
        [TestCase("identity:opaque -t 1 -e 1.1 -v CTEyNy4wLjAuMeouAAAQJwAAAA==",
                  "identity:tcp -h 127.0.0.1 -p 12010 -t 10000")]
        [TestCase("identity:opaque -t 1 -e 1.0 -v CTEyNy4wLjAuMeouAAAQJwAAAA==",
                  "identity:tcp -h 127.0.0.1 -p 12010 -t 10000")]
        public async Task Operations_ServiceAsync(string proxy, string? actualIceProxy = null)
        {
            IProxyFormat? format = proxy.StartsWith("ice", StringComparison.Ordinal) ?
                null : IceProxyFormat.Default;

            var service = ServicePrx.Parse(proxy, format: format);
            ServicePrx result = await _prx.OpServiceAsync(service);

            if (_prx.Proxy.Protocol == Protocol.Ice && actualIceProxy != null)
            {
                var actual = ServicePrx.Parse(actualIceProxy, format: format);
                Assert.That(result, Is.EqualTo(actual));
            }
            else
            {
                Assert.That(result, Is.EqualTo(service));
            }
        }

        [Test]
        public async Task Operations_OperationNotFoundExceptionAsync()
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher, NoOperations>()
                .BuildServiceProvider();
            var prx = OperationsPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());
            var dispatchException = Assert.ThrowsAsync<DispatchException>(() => prx.OpBoolAsync(true, false));
            Assert.That(dispatchException!.ErrorCode, Is.EqualTo(DispatchErrorCode.OperationNotFound));
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
