// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.CodeGeneration
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture(Protocol.Ice1)]
    [TestFixture(Protocol.Ice2)]
    public class MarshaledResultTests
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly IMarshaledResultOperationsPrx _prx;

        public MarshaledResultTests(Protocol protocol)
        {
            _communicator = new Communicator();
            _server = new Server
            {
                Communicator = _communicator,
                Dispatcher = new MarshaledResultOperations(),
                Protocol = protocol
            };
            _server.Listen();
            _prx = _server.CreateRelativeProxy<IMarshaledResultOperationsPrx>("/test");
            Assert.AreEqual(protocol, _prx.Protocol);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
        }

        [Test]
        public async Task Invocation_MarshalledResultAsync()
        {
            await Test1Async(p1 => _prx.OpAnotherStruct1Async(p1),
                             new AnotherStruct("hello",
                                              IOperationsPrx.Parse("ice+tcp://host/foo", _communicator),
                                              MyEnum.enum1,
                                              new MyStruct(1, 2)));

            await Test1Async(p1 => _prx.OpStringSeq1Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToArray());

            await Test1Async(p1 => _prx.OpStringDict1Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToDictionary(key => key,
                                                                                            value => value));

            await Test2Async(p1 => _prx.OpAnotherStruct2Async(p1),
                            new AnotherStruct("hello",
                                              IOperationsPrx.Parse("ice+tcp://host/foo", _communicator),
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

        public class MarshaledResultOperations : IAsyncMarshaledResultOperations
        {
            // Marshalled result
            public ValueTask<IMarshaledResultOperations.OpAnotherStruct1MarshaledReturnValue> OpAnotherStruct1Async(
                AnotherStruct p1,
                Current current,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpAnotherStruct1MarshaledReturnValue(p1, current));

            public ValueTask<IMarshaledResultOperations.OpAnotherStruct2MarshaledReturnValue> OpAnotherStruct2Async(
                AnotherStruct p1,
                Current current,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpAnotherStruct2MarshaledReturnValue(p1, p1, current));

            public ValueTask<IMarshaledResultOperations.OpStringSeq1MarshaledReturnValue> OpStringSeq1Async(
                string[] p1,
                Current current,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpStringSeq1MarshaledReturnValue(p1, current));

            public ValueTask<IMarshaledResultOperations.OpStringSeq2MarshaledReturnValue> OpStringSeq2Async(
                string[] p1,
                Current current,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpStringSeq2MarshaledReturnValue(p1, p1, current));

            public ValueTask<IMarshaledResultOperations.OpStringDict1MarshaledReturnValue> OpStringDict1Async(
                Dictionary<string, string> p1,
                Current current,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpStringDict1MarshaledReturnValue(p1, current));

            public ValueTask<IMarshaledResultOperations.OpStringDict2MarshaledReturnValue> OpStringDict2Async(
                Dictionary<string, string> p1,
                Current current,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpStringDict2MarshaledReturnValue(p1, p1, current));
        }
    }
}
