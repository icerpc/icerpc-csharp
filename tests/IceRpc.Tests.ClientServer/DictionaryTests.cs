// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture(Protocol.Ice1)]
    [TestFixture(Protocol.Ice2)]
    public class DictionaryTests : ClientServerBaseTest
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly IDictionaryOperationsPrx _prx;

        public DictionaryTests(Protocol protocol)
        {
            _communicator = new Communicator();
            _server = new Server(_communicator,
                new ServerOptions()
                {
                    Protocol = protocol,
                    ColocationScope = ColocationScope.Communicator
                });
            _prx = _server.Add("test", new DictionaryOperations(), IDictionaryOperationsPrx.Factory);
            Assert.AreEqual(protocol, _prx.Protocol);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
        }

        [Test]
        public async Task Dictionary_BuiltinTypes()
        {
            int size = 100;
            await TestDictAsync((prx, p1, p2) => prx.OpByteDictAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => (byte)i).ToDictionary(key => key, value => value),
                                Enumerable.Range(0, size).Select(i => (byte)i).ToDictionary(key => key, value => value));
            await TestDictAsync((prx, p1, p2) => prx.OpBoolDictAsync(p1, p2),
                                Enumerable.Range(0, 2).Select(i => i % 2 == 0).ToDictionary(key => key, value => value),
                                Enumerable.Range(0, 2).Select(i => i % 2 == 0).ToDictionary(key => key, value => value));
            await TestDictAsync((prx, p1, p2) => prx.OpShortDictAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => (short)i).ToDictionary(key => key, value => value),
                                Enumerable.Range(0, size).Select(i => (short)i).ToDictionary(key => key, value => value));
            await TestDictAsync((prx, p1, p2) => prx.OpUShortDictAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => (ushort)i).ToDictionary(key => key, value => value),
                                Enumerable.Range(0, size).Select(i => (ushort)i).ToDictionary(key => key, value => value));
            await TestDictAsync((prx, p1, p2) => prx.OpIntDictAsync(p1, p2),
                                Enumerable.Range(0, size).ToDictionary(key => key, value => value),
                                Enumerable.Range(0, size).ToDictionary(key => key, value => value));
            await TestDictAsync((prx, p1, p2) => prx.OpVarIntDictAsync(p1, p2),
                                Enumerable.Range(0, size).ToDictionary(key => key, value => value),
                                Enumerable.Range(0, size).ToDictionary(key => key, value => value));
            await TestDictAsync((prx, p1, p2) => prx.OpUIntDictAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value),
                                Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value));
            await TestDictAsync((prx, p1, p2) => prx.OpVarUIntDictAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value),
                               Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value));
            await TestDictAsync((prx, p1, p2) => prx.OpLongDictAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value),
                                Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value));
            await TestDictAsync((prx, p1, p2) => prx.OpVarLongDictAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value),
                                Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value));
            await TestDictAsync((prx, p1, p2) => prx.OpULongDictAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value),
                               Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value));
            await TestDictAsync((prx, p1, p2) => prx.OpVarULongDictAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value),
                                Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value));
            await TestDictAsync((prx, p1, p2) => prx.OpStringDictAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => $"hello-{i}").ToDictionary(key => key, value => value),
                                Enumerable.Range(0, size).Select(i => $"hello-{i}").ToDictionary(key => key, value => value));

            async Task TestDictAsync<T>(
                Func<IDictionaryOperationsPrx, Dictionary<T, T>, Dictionary<T, T>, Task<(Dictionary<T, T>, Dictionary<T, T>)>> invoker,
                Dictionary<T, T> p1,
                Dictionary<T, T> p2) where T : notnull
            {
                (Dictionary<T, T> r1, Dictionary<T, T> r2) = await invoker(_prx, p1, p2);
                CollectionAssert.AreEqual(r1, p1);
                CollectionAssert.AreEqual(r2, p2);
            }
        }

        public class DictionaryOperations : IAsyncDictionaryOperations
        {
            // Builtin types dictionaries
            public ValueTask<(IReadOnlyDictionary<byte, byte> R1, IReadOnlyDictionary<byte, byte> R2)> OpByteDictAsync(
                Dictionary<byte, byte> p1,
                Dictionary<byte, byte> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<bool, bool> R1, IReadOnlyDictionary<bool, bool> R2)> OpBoolDictAsync(
                Dictionary<bool, bool> p1,
                Dictionary<bool, bool> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<short, short> R1, IReadOnlyDictionary<short, short> R2)> OpShortDictAsync(
                Dictionary<short, short> p1,
                Dictionary<short, short> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<ushort, ushort> R1, IReadOnlyDictionary<ushort, ushort> R2)> OpUShortDictAsync(
                Dictionary<ushort, ushort> p1,
                Dictionary<ushort, ushort> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<int, int> R1, IReadOnlyDictionary<int, int> R2)> OpIntDictAsync(
                Dictionary<int, int> p1,
                Dictionary<int, int> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<int, int> R1, IReadOnlyDictionary<int, int> R2)> OpVarIntDictAsync(
                Dictionary<int, int> p1,
                Dictionary<int, int> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<uint, uint> R1, IReadOnlyDictionary<uint, uint> R2)> OpUIntDictAsync(
                Dictionary<uint, uint> p1,
                Dictionary<uint, uint> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<uint, uint> R1, IReadOnlyDictionary<uint, uint> R2)> OpVarUIntDictAsync(
                Dictionary<uint, uint> p1,
                Dictionary<uint, uint> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<long, long> R1, IReadOnlyDictionary<long, long> R2)> OpLongDictAsync(
                Dictionary<long, long> p1,
                Dictionary<long, long> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<long, long> R1, IReadOnlyDictionary<long, long> R2)> OpVarLongDictAsync(
                Dictionary<long, long> p1,
                Dictionary<long, long> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<ulong, ulong> R1, IReadOnlyDictionary<ulong, ulong> R2)> OpULongDictAsync(
                Dictionary<ulong, ulong> p1,
                Dictionary<ulong, ulong> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<ulong, ulong> R1, IReadOnlyDictionary<ulong, ulong> R2)> OpVarULongDictAsync(
                Dictionary<ulong, ulong> p1,
                Dictionary<ulong, ulong> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<string, string> R1, IReadOnlyDictionary<string, string> R2)> OpStringDictAsync(
                Dictionary<string, string> p1,
                Dictionary<string, string> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));
        }
    }
}
