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
    public class StructTests
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly IStructOperationsPrx _prx;

        public StructTests(Protocol protocol)
        {
            _communicator = new Communicator();
            _server = new Server
            {
                Communicator = _communicator,
                Dispatcher = new StructOperations(),
                Protocol = protocol
            };
            _server.Listen();
            _prx = _server.CreateRelativeProxy<IStructOperationsPrx>("/test");
            Assert.AreEqual(protocol, _prx.Protocol);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
        }

        [Test]
        public async Task Struct_OperationsAsync()
        {
            await TestAsync((p1, p2) => _prx.OpMyStructAsync(p1, p2), new MyStruct(1, 2), new MyStruct(3, 4));
            await TestAsync((p1, p2) => _prx.OpAnotherStructAsync(p1, p2),
                            new AnotherStruct("hello",
                                              IOperationsPrx.Parse("ice+tcp://host/foo", _communicator),
                                              MyEnum.enum1,
                                              new MyStruct(1, 2)),
                            new AnotherStruct("world",
                                              IOperationsPrx.Parse("ice+tcp://host/bar", _communicator),
                                              MyEnum.enum2,
                                              new MyStruct(3, 4)));

            static async Task TestAsync<T>(Func<T, T, Task<(T, T)>> invoker, T p1, T p2)
            {
                (T r1, T r2) = await invoker(p1, p2);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p2, r2);
            }
        }

        public class StructOperations : IAsyncStructOperations
        {
            public ValueTask<(MyStruct R1, MyStruct R2)> OpMyStructAsync(
                MyStruct p1,
                MyStruct p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(AnotherStruct R1, AnotherStruct R2)> OpAnotherStructAsync(
                AnotherStruct p1,
                AnotherStruct p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));
        }
    }
}
