// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture(ProtocolCode.Ice1)]
    [TestFixture(ProtocolCode.Ice2)]
    public sealed class EncodedResultTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly EncodedResultOperationsPrx _prx;

        public EncodedResultTests(ProtocolCode protocol)
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
            await Test1Async(p1 => _prx.OpAnotherStruct1Async(p1),
                             new AnotherStruct("hello",
                                              OperationsPrx.Parse("icerpc+tcp://foo/bar"),
                                              MyEnum.enum1,
                                              new MyStruct(1, 2)));

            await Test1Async(p1 => _prx.OpStringSeq1Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToArray());

            await Test1Async(p1 => _prx.OpStringDict1Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToDictionary(key => key,
                                                                                            value => value));

            await Test2Async(p1 => _prx.OpAnotherStruct2Async(p1),
                            new AnotherStruct("hello",
                                              OperationsPrx.Parse("icerpc+tcp://foo/bar"),
                                              MyEnum.enum1,
                                              new MyStruct(1, 2)));

            await Test2Async(p1 => _prx.OpStringSeq2Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToArray());

            await Test2Async(p1 => _prx.OpStringDict2Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToDictionary(key => key,
                                                                                            value => value));

            async Task Test1Async<T>(Func<T, Task<T>> invoker, T p1)
            {
                T r1 = await invoker(p1);
                Assert.AreEqual(p1, r1);
            }

            async Task Test2Async<T>(Func<T, Task<(T, T)>> invoker, T p1)
            {
                (T r1, T r2) = await invoker(p1);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p1, r2);
            }
        }

        public class EncodedResultOperations : Service, IEncodedResultOperations
        {
            // Encoded result
            public ValueTask<IEncodedResultOperations.OpAnotherStruct1EncodedResult> OpAnotherStruct1Async(
                AnotherStruct p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IEncodedResultOperations.OpAnotherStruct1EncodedResult(p1, dispatch));

            public ValueTask<IEncodedResultOperations.OpAnotherStruct2EncodedResult> OpAnotherStruct2Async(
                AnotherStruct p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IEncodedResultOperations.OpAnotherStruct2EncodedResult(p1, p1, dispatch));

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
