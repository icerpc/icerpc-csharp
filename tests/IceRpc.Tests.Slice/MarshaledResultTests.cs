// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture(ProtocolCode.Ice1)]
    [TestFixture(ProtocolCode.Ice2)]
    public sealed class MarshaledResultTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly MarshaledResultOperationsPrx _prx;

        public MarshaledResultTests(ProtocolCode protocol)
        {
            Endpoint serverEndpoint = TestHelper.GetUniqueColocEndpoint(Protocol.FromProtocolCode(protocol));
            _server = new Server
            {
                Dispatcher = new MarshaledResultOperations(),
                Endpoint = serverEndpoint
            };
            _server.Listen();
            _connection = new Connection
            {
                RemoteEndpoint = serverEndpoint
            };
            _prx = MarshaledResultOperationsPrx.FromConnection(_connection);
            Assert.AreEqual(protocol, _prx.Proxy.Protocol.Code);
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
            public ValueTask<IMarshaledResultOperations.OpAnotherStruct1EncodedReturnValue> OpAnotherStruct1Async(
                AnotherStruct p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpAnotherStruct1EncodedReturnValue(p1, dispatch));

            public ValueTask<IMarshaledResultOperations.OpAnotherStruct2EncodedReturnValue> OpAnotherStruct2Async(
                AnotherStruct p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpAnotherStruct2EncodedReturnValue(p1, p1, dispatch));

            public ValueTask<IMarshaledResultOperations.OpStringSeq1EncodedReturnValue> OpStringSeq1Async(
                string[] p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpStringSeq1EncodedReturnValue(p1, dispatch));

            public ValueTask<IMarshaledResultOperations.OpStringSeq2EncodedReturnValue> OpStringSeq2Async(
                string[] p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpStringSeq2EncodedReturnValue(p1, p1, dispatch));

            public ValueTask<IMarshaledResultOperations.OpStringDict1EncodedReturnValue> OpStringDict1Async(
                Dictionary<string, string> p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpStringDict1EncodedReturnValue(p1, dispatch));

            public ValueTask<IMarshaledResultOperations.OpStringDict2EncodedReturnValue> OpStringDict2Async(
                Dictionary<string, string> p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpStringDict2EncodedReturnValue(p1, p1, dispatch));

            public ValueTask<IMarshaledResultOperations.OpMyClassAEncodedReturnValue> OpMyClassAAsync(
                MyClassA p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(new IMarshaledResultOperations.OpMyClassAEncodedReturnValue(p1));
        }
    }
}
