// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests.Slice
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
        private readonly StructOperationsPrx _prx;

        public StructTests(Protocol protocol)
        {
            Endpoint serverEndpoint = TestHelper.GetUniqueColocEndpoint(protocol);
            _server = new Server
            {
                Dispatcher = new StructOperations(),
                Endpoint = serverEndpoint
            };
            _server.Listen();
            _connection = new Connection
            {
                RemoteEndpoint = serverEndpoint
            };
            _prx = StructOperationsPrx.FromConnection(_connection);
            Assert.AreEqual(protocol, _prx.Proxy.Protocol);

            Assert.AreEqual("12", new MyStruct(5, 7).ToString());
        }

        [TearDown]
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
                                              OperationsPrx.Parse("ice+tcp://foo/bar"),
                                              MyEnum.enum1,
                                              new MyStruct(1, 2)),
                            new AnotherStruct("world",
                                              OperationsPrx.Parse("ice+tcp://foo/bar"),
                                              MyEnum.enum2,
                                              new MyStruct(3, 4)));

            static async Task TestAsync<T>(Func<T, T, Task<(T, T)>> invoker, T p1, T p2)
            {
                (T r1, T r2) = await invoker(p1, p2);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p2, r2);
            }
        }

        public class StructOperations : Service, IStructOperations
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

    public partial record struct MyStruct
    {
        // Test overrides

        public readonly bool Equals(MyStruct other) => I == other.I;

        public override readonly int GetHashCode() => I.GetHashCode();

        public override readonly string ToString() => $"{I + J}";
    }
}
