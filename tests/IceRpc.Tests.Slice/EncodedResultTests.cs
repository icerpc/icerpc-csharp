// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("ice")]
    [TestFixture("icerpc")]
    public sealed class EncodedResultTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly EncodedResultOperationsPrx _prx;

        public EncodedResultTests(string protocol)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher, EncodedResultOperations>()
                .BuildServiceProvider();
            _prx = EncodedResultOperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task Invocation_EncodedResultAsync()
        {
            // TODO Parse below should not use a connection with a different endpoint
            await Test1Async(p1 => _prx.OpAnotherCompactStruct1Async(p1),
                             new AnotherCompactStruct("hello",
                                              OperationsPrx.Parse("icerpc://foo/bar"),
                                              MyEnum.enum1,
                                              new MyCompactStruct(1, 2)));

            await Test1Async(p1 => _prx.OpStringSeq1Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToArray());

            await Test1Async(p1 => _prx.OpStringDict1Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToDictionary(key => key,
                                                                                            value => value));

            await Test2Async(p1 => _prx.OpAnotherCompactStruct2Async(p1),
                            new AnotherCompactStruct("hello",
                                              OperationsPrx.Parse("icerpc://foo/bar"),
                                              MyEnum.enum1,
                                              new MyCompactStruct(1, 2)));

            await Test2Async(p1 => _prx.OpStringSeq2Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToArray());

            await Test2Async(p1 => _prx.OpStringDict2Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToDictionary(key => key,
                                                                                            value => value));

            async Task Test1Async<T>(Func<T, Task<T>> invoker, T p1)
            {
                T r1 = await invoker(p1);
                Assert.That(r1, Is.EqualTo(p1));
            }

            async Task Test2Async<T>(Func<T, Task<(T, T)>> invoker, T p1)
            {
                (T r1, T r2) = await invoker(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }
        }

        public class EncodedResultOperations : Service, IEncodedResultOperations
        {
            // Encoded result
            public ValueTask<IEncodedResultOperations.OpAnotherCompactStruct1EncodedResult> OpAnotherCompactStruct1Async(
                AnotherCompactStruct p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IEncodedResultOperations.OpAnotherCompactStruct1EncodedResult(p1, dispatch));

            public ValueTask<IEncodedResultOperations.OpAnotherCompactStruct2EncodedResult> OpAnotherCompactStruct2Async(
                AnotherCompactStruct p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IEncodedResultOperations.OpAnotherCompactStruct2EncodedResult(p1, p1, dispatch));

            public ValueTask<IEncodedResultOperations.OpStringSeq1EncodedResult> OpStringSeq1Async(
                string[] p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IEncodedResultOperations.OpStringSeq1EncodedResult(p1, dispatch));

            public ValueTask<IEncodedResultOperations.OpStringSeq2EncodedResult> OpStringSeq2Async(
                string[] p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IEncodedResultOperations.OpStringSeq2EncodedResult(p1, p1, dispatch));

            public ValueTask<IEncodedResultOperations.OpStringDict1EncodedResult> OpStringDict1Async(
                Dictionary<string, string> p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IEncodedResultOperations.OpStringDict1EncodedResult(p1, dispatch));

            public ValueTask<IEncodedResultOperations.OpStringDict2EncodedResult> OpStringDict2Async(
                Dictionary<string, string> p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IEncodedResultOperations.OpStringDict2EncodedResult(p1, p1, dispatch));

            public ValueTask<IEncodedResultOperations.OpMyClassAEncodedResult> OpMyClassAAsync(
                MyClassA p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IEncodedResultOperations.OpMyClassAEncodedResult(p1));
        }
    }
}
