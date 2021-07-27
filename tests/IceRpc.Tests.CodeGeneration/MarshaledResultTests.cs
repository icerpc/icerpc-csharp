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
    public sealed class MarshaledResultTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly MarshaledResultOperationsPrx _prx;

        public MarshaledResultTests(Protocol protocol)
        {
            _server = new Server
            {
                Dispatcher = new MarshaledResultOperations(),
                Endpoint = TestHelper.GetUniqueColocEndpoint(protocol)
            };
            _server.Listen();
            _connection = new Connection { RemoteEndpoint = _server.Endpoint };
            _prx = MarshaledResultOperationsPrx.FromConnection(_connection);
            Assert.AreEqual(protocol, _prx.Proxy.Protocol);
        }

        [TearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Test]
        public async Task Invocation_MarshalledResultAsync()
        {
            // TODO Parse below should not use a connection with a different endpoint
            await Test1Async(p1 => _prx.OpAnotherStruct1Async(p1),
                             new AnotherStruct("hello",
                                              OperationsPrx.Parse("ice+tcp://foo/bar"),
                                              MyEnum.enum1,
                                              new MyStruct(1, 2)));

            await Test1Async(p1 => _prx.OpStringSeq1Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToArray());

            await Test1Async(p1 => _prx.OpStringDict1Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToDictionary(key => key,
                                                                                            value => value));

            await Test2Async(p1 => _prx.OpAnotherStruct2Async(p1),
                            new AnotherStruct("hello",
                                              OperationsPrx.Parse("ice+tcp://foo/bar"),
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

        public class MarshaledResultOperations : Service, IMarshaledResultOperations
        {
            // Marshalled result
            public ValueTask<IMarshaledResultOperations.OpAnotherStruct1MarshaledReturnValue> OpAnotherStruct1Async(
                AnotherStruct p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpAnotherStruct1MarshaledReturnValue(p1, dispatch));

            public ValueTask<IMarshaledResultOperations.OpAnotherStruct2MarshaledReturnValue> OpAnotherStruct2Async(
                AnotherStruct p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpAnotherStruct2MarshaledReturnValue(p1, p1, dispatch));

            public ValueTask<IMarshaledResultOperations.OpStringSeq1MarshaledReturnValue> OpStringSeq1Async(
                string[] p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpStringSeq1MarshaledReturnValue(p1, dispatch));

            public ValueTask<IMarshaledResultOperations.OpStringSeq2MarshaledReturnValue> OpStringSeq2Async(
                string[] p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpStringSeq2MarshaledReturnValue(p1, p1, dispatch));

            public ValueTask<IMarshaledResultOperations.OpStringDict1MarshaledReturnValue> OpStringDict1Async(
                Dictionary<string, string> p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpStringDict1MarshaledReturnValue(p1, dispatch));

            public ValueTask<IMarshaledResultOperations.OpStringDict2MarshaledReturnValue> OpStringDict2Async(
                Dictionary<string, string> p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpStringDict2MarshaledReturnValue(p1, p1, dispatch));
        }
    }
}
