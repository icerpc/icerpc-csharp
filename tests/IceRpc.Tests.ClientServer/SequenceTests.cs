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
    public class SequenceTests : ClientServerBaseTest
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly ISequenceOperationsPrx _prx;

        public SequenceTests(Protocol protocol)
        {
            _communicator = new Communicator();
            _server = new Server(_communicator,
                new ServerOptions()
                {
                    Protocol = protocol,
                    ColocationScope = ColocationScope.Communicator
                });
            _prx = _server.Add("test", new SequenceOperations(), ISequenceOperationsPrx.Factory);
            Assert.AreEqual(protocol, _prx.Protocol);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
        }

        [Test]
        public async Task Sequence_BuiltinTypes()
        {
            int size = 100;
            await TestSeqAsync((prx, p1, p2) => prx.OpByteSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (byte)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (byte)i).Reverse().ToArray());
            await TestSeqAsync((prx, p1, p2) => prx.OpBoolSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => i % 2 == 0).ToArray(),
                               Enumerable.Range(0, size).Select(i => i % 2 == 0).Reverse().ToArray());
            await TestSeqAsync((prx, p1, p2) => prx.OpShortSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (short)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (short)i).Reverse().ToArray());
            await TestSeqAsync((prx, p1, p2) => prx.OpUShortSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (ushort)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (ushort)i).Reverse().ToArray());
            await TestSeqAsync((prx, p1, p2) => prx.OpIntSeqAsync(p1, p2),
                               Enumerable.Range(0, size).ToArray(),
                               Enumerable.Range(0, size).Reverse().ToArray());
            await TestVarSeqAsync((prx, p1, p2) => prx.OpVarIntSeqAsync(p1, p2),
                                  Enumerable.Range(0, size).ToArray(),
                                  Enumerable.Range(0, size).Reverse().ToArray());
            await TestSeqAsync((prx, p1, p2) => prx.OpUIntSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (uint)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (uint)i).Reverse().ToArray());
            await TestVarSeqAsync((prx, p1, p2) => prx.OpVarUIntSeqAsync(p1, p2),
                                  Enumerable.Range(0, size).Select(i => (uint)i).ToArray(),
                                  Enumerable.Range(0, size).Select(i => (uint)i).Reverse().ToArray());
            await TestSeqAsync((prx, p1, p2) => prx.OpLongSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (long)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (long)i).Reverse().ToArray());
            await TestVarSeqAsync((prx, p1, p2) => prx.OpVarLongSeqAsync(p1, p2),
                                  Enumerable.Range(0, size).Select(i => (long)i).ToArray(),
                                  Enumerable.Range(0, size).Select(i => (long)i).Reverse().ToArray());
            await TestSeqAsync((prx, p1, p2) => prx.OpULongSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (ulong)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (ulong)i).Reverse().ToArray());
            await TestVarSeqAsync((prx, p1, p2) => prx.OpVarULongSeqAsync(p1, p2),
                                  Enumerable.Range(0, size).Select(i => (ulong)i).ToArray(),
                                  Enumerable.Range(0, size).Select(i => (ulong)i).Reverse().ToArray());
            await TestSeqAsync((prx, p1, p2) => prx.OpFloatSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (float)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (float)i).Reverse().ToArray());
            await TestSeqAsync((prx, p1, p2) => prx.OpDoubleSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (double)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (double)i).Reverse().ToArray());
            await TestVarSeqAsync((prx, p1, p2) => prx.OpStringSeqAsync(p1, p2),
                                  Enumerable.Range(0, size).Select(i => $"hello-{i}").ToArray(),
                                  Enumerable.Range(0, size).Select(i => $"hello-{i}").Reverse().ToArray());

            async Task TestSeqAsync<T>(
                Func<ISequenceOperationsPrx, ReadOnlyMemory<T>, ReadOnlyMemory<T>, Task<(T[], T[])>> invoker,
                T[] p1,
                T[] p2)
            {
                (T[] r1, T[] r2) = await invoker(_prx, p1, p2);
                CollectionAssert.AreEqual(r1, p1);
                CollectionAssert.AreEqual(r2, p2);
            }

            async Task TestVarSeqAsync<T>(
                Func<ISequenceOperationsPrx, IEnumerable<T>, IEnumerable<T>, Task<(T[], T[])>> invoker,
                T[] p1,
                T[] p2)
            {
                (T[] r1, T[] r2) = await invoker(_prx, p1, p2);
                CollectionAssert.AreEqual(r1, p1);
                CollectionAssert.AreEqual(r2, p2);
            }
        }

        public class SequenceOperations : IAsyncSequenceOperations
        {
            // Builtin type sequences

            public ValueTask<(ReadOnlyMemory<byte> R1, ReadOnlyMemory<byte> R2)> OpByteSeqAsync(
                byte[] p1,
                byte[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<bool> R1, ReadOnlyMemory<bool> R2)> OpBoolSeqAsync(
                bool[] p1,
                bool[] p2,
                Current current, CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<short> R1, ReadOnlyMemory<short> R2)> OpShortSeqAsync(
                short[] p1,
                short[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<ushort> R1, ReadOnlyMemory<ushort> R2)> OpUShortSeqAsync(
                ushort[] p1,
                ushort[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<int> R1, ReadOnlyMemory<int> R2)> OpIntSeqAsync(
                int[] p1,
                int[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<int> R1, IEnumerable<int> R2)> OpVarIntSeqAsync(
                int[] p1,
                int[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<uint> R1, ReadOnlyMemory<uint> R2)> OpUIntSeqAsync(
                uint[] p1,
                uint[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<uint> R1, IEnumerable<uint> R2)> OpVarUIntSeqAsync(
                uint[] p1,
                uint[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<long> R1, ReadOnlyMemory<long> R2)> OpLongSeqAsync(
                long[] p1,
                long[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<long> R1, IEnumerable<long> R2)> OpVarLongSeqAsync(
                long[] p1,
                long[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<ulong> R1, ReadOnlyMemory<ulong> R2)> OpULongSeqAsync(
                ulong[] p1,
                ulong[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<ulong> R1, IEnumerable<ulong> R2)> OpVarULongSeqAsync(
                ulong[] p1,
                ulong[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<float> R1, ReadOnlyMemory<float> R2)> OpFloatSeqAsync(
                float[] p1,
                float[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<double> R1, ReadOnlyMemory<double> R2)> OpDoubleSeqAsync(
                double[] p1,
                double[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<string> R1, IEnumerable<string> R2)> OpStringSeqAsync(
                string[] p1,
                string[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));
        }
    }
}
