// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.CodeGeneration
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture(Protocol.Ice1)]
    [TestFixture(Protocol.Ice2)]
    public sealed class StructTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly IStructOperationsPrx _prx;

        public StructTests(Protocol protocol)
        {
            _server = new Server
            {
                Dispatcher = new StructOperations(),
                Endpoint = TestHelper.GetUniqueColocEndpoint(protocol)
            };
            _server.Listen();
            _connection = new Connection { RemoteEndpoint = _server.ProxyEndpoint };
            _prx = IStructOperationsPrx.FromConnection(_connection);
            Assert.AreEqual(protocol, _prx.Protocol);
        }

        [TearDown]
        public async Task TearDownAsync() => await DisposeAsync();

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Structure",
            "NUnit1028:The non-test method is public",
            Justification = "IAsyncDispoable implementation")]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Test]
        public async Task Struct_OperationsAsync()
        {
            // TODO Parse below should not use a connection with a different endpoint
            await TestAsync((p1, p2) => _prx.OpMyStructAsync(p1, p2), new MyStruct(1, 2), new MyStruct(3, 4));
            await TestAsync((p1, p2) => _prx.OpAnotherStructAsync(p1, p2),
                            new AnotherStruct("hello",
                                              IOperationsPrx.Parse("ice+tcp://foo/bar"),
                                              MyEnum.enum1,
                                              new MyStruct(1, 2)),
                            new AnotherStruct("world",
                                              IOperationsPrx.Parse("ice+tcp://foo/bar"),
                                              MyEnum.enum2,
                                              new MyStruct(3, 4)));

            static async Task TestAsync<T>(Func<T, T, Task<(T, T)>> invoker, T p1, T p2)
            {
                (T r1, T r2) = await invoker(p1, p2);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p2, r2);
            }
        }

        public class StructOperations : IStructOperations
        {
            public ValueTask<(MyStruct R1, MyStruct R2)> OpMyStructAsync(
                MyStruct p1,
                MyStruct p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(AnotherStruct R1, AnotherStruct R2)> OpAnotherStructAsync(
                AnotherStruct p1,
                AnotherStruct p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));
        }
    }
}
