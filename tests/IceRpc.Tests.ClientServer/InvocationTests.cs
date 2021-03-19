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
    public class InvocationTests : ClientServerBaseTest
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly IInvocationServicePrx _prx;
        private readonly IDerivedInvocationServicePrx _derivedPrx;

        public InvocationTests(Protocol protocol)
        {
            _communicator = new Communicator();
            _server = new Server(_communicator,
                new ServerOptions()
                {
                    Protocol = protocol,
                    ColocationScope = ColocationScope.Communicator
                });
            _prx = _server.Add("test", new InvocationService(), IInvocationServicePrx.Factory);
            _derivedPrx = _prx.WithPath<IDerivedInvocationServicePrx>("test");
            Assert.AreEqual(protocol, _prx.Protocol);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
        }

        [Test]
        public async Task Invocation_BuiltinTypes()
        {
            await TestAsync((prx, p1, p2) => prx.OpByteAsync(p1, p2), byte.MinValue, byte.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpBoolAsync(p1, p2), false, true);
            await TestAsync((prx, p1, p2) => prx.OpShortAsync(p1, p2), short.MinValue, short.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpUShortAsync(p1, p2), ushort.MinValue, ushort.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpIntAsync(p1, p2), int.MinValue, int.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpVarIntAsync(p1, p2), int.MinValue, int.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpUIntAsync(p1, p2), uint.MinValue, uint.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpVarUIntAsync(p1, p2), uint.MinValue, uint.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpLongAsync(p1, p2), long.MinValue, long.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpVarLongAsync(p1, p2), int.MinValue, (long)int.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpULongAsync(p1, p2), ulong.MinValue, ulong.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpVarULongAsync(p1, p2), ulong.MinValue, (ulong)uint.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpFloatAsync(p1, p2), float.MinValue, float.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpDoubleAsync(p1, p2), double.MinValue, double.MaxValue);
            await TestAsync((prx, p1, p2) => prx.OpStringAsync(p1, p2), "hello", "world");

            async Task TestAsync<T>(Func<IInvocationServicePrx, T, T, Task<(T, T)>> invoker, T p1, T p2)
            {
                foreach (IInvocationServicePrx prx in new IInvocationServicePrx[] { _prx, _derivedPrx})
                {
                    (T r1, T r2) = await invoker(prx, p1, p2);
                    Assert.AreEqual(p1, r1);
                    Assert.AreEqual(p2, r2);
                }
            }
        }

        [Test]
        public async Task Invocation_BuiltinTypeSequences()
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
                Func<IInvocationServicePrx, ReadOnlyMemory<T>, ReadOnlyMemory<T>, Task<(T[], T[])>> invoker,
                T[] p1,
                T[] p2)
            {
                foreach (IInvocationServicePrx prx in new IInvocationServicePrx[] { _prx, _derivedPrx })
                {
                    (T[] r1, T[] r2) = await invoker(prx, p1, p2);
                    CollectionAssert.AreEqual(r1, p1);
                    CollectionAssert.AreEqual(r2, p2);
                }
            }

            async Task TestVarSeqAsync<T>(
                Func<IInvocationServicePrx, IEnumerable<T>, IEnumerable<T>, Task<(T[], T[])>> invoker,
                T[] p1,
                T[] p2)
            {
                foreach (IInvocationServicePrx prx in new IInvocationServicePrx[] { _prx, _derivedPrx })
                {
                    (T[] r1, T[] r2) = await invoker(prx, p1, p2);
                    CollectionAssert.AreEqual(r1, p1);
                    CollectionAssert.AreEqual(r2, p2);
                }
            }
        }

        [Test]
        public async Task Invocation_BuiltinTypeDictionaries()
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
                Func<IInvocationServicePrx, Dictionary<T, T>, Dictionary<T, T>, Task<(Dictionary<T, T>, Dictionary<T, T>)>> invoker,
                Dictionary<T, T> p1,
                Dictionary<T, T> p2) where T : notnull
            {
                foreach (IInvocationServicePrx prx in new IInvocationServicePrx[] { _prx, _derivedPrx })
                {
                    (Dictionary<T, T> r1, Dictionary<T, T> r2) = await invoker(prx, p1, p2);
                    CollectionAssert.AreEqual(r1, p1);
                    CollectionAssert.AreEqual(r2, p2);
                }
            }
        }

        [Test]
        public async Task Invocation_Defined()
        {
            await TestAsync((prx, p1, p2) => prx.OpMyEnumAsync(p1, p2), MyEnum.enum1, MyEnum.enum2);
            await TestAsync((prx, p1, p2) => prx.OpMyStructAsync(p1, p2), new MyStruct(1, 2), new MyStruct(3, 4));
            await TestAsync((prx, p1, p2) => prx.OpAnotherStructAsync(p1, p2),
                            new AnotherStruct("hello",
                                              IInvocationServicePrx.Parse("foo", _communicator),
                                              MyEnum.enum1,
                                              new MyStruct(1, 2)),
                            new AnotherStruct("world",
                                              IInvocationServicePrx.Parse("bar", _communicator),
                                              MyEnum.enum2,
                                              new MyStruct(3, 4)));

            async Task TestAsync<T>(Func<IInvocationServicePrx, T, T, Task<(T, T)>> invoker, T p1, T p2)
            {
                foreach (IInvocationServicePrx prx in new IInvocationServicePrx[] { _prx, _derivedPrx })
                {
                    (T r1, T r2) = await invoker(_prx, p1, p2);
                    Assert.AreEqual(p1, r1);
                    Assert.AreEqual(p2, r2);
                }
            }
        }

        [Test]
        public async Task Invocation_DefinedSequences()
        {
            int size = 100;
            await TestAsync((prx, p1, p2) => prx.OpMyEnumSeqAsync(p1, p2),
                            Enumerable.Range(0, size).Select(i => (MyEnum)(i % 3)).ToArray(),
                            Enumerable.Range(0, size).Select(i => (MyEnum)(i % 3)).Reverse().ToArray());

            await TestAsync((prx, p1, p2) => prx.OpMyStructSeqAsync(p1, p2),
                            Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1)).ToArray(),
                            Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1)).Reverse().ToArray());

            await TestAsync((prx, p1, p2) => prx.OpAnotherStructSeqAsync(p1, p2),
                            Enumerable.Range(0, size).Select(i =>
                            {
                                return new AnotherStruct($"hello-{i}",
                                                         IInvocationServicePrx.Parse($"foo-{i}", _communicator),
                                                         (MyEnum)(i % 3),
                                                         new MyStruct(i, i + 1));
                            }).ToArray(),
                            Enumerable.Range(0, size).Select(i =>
                            {
                                return new AnotherStruct($"world-{i}",
                                                         IInvocationServicePrx.Parse($"bar-{i}", _communicator),
                                                         (MyEnum)(i % 3),
                                                         new MyStruct(i, i + 1));
                            }).Reverse().ToArray());

            async Task TestAsync<T>(
                Func<IInvocationServicePrx, IEnumerable<T>, IEnumerable<T>, Task<(T[], T[])>> invoker,
                T[] p1,
                T[] p2)
            {
                foreach (IInvocationServicePrx prx in new IInvocationServicePrx[] { _prx, _derivedPrx })
                {
                    (T[] r1, T[] r2) = await invoker(_prx, p1, p2);
                    CollectionAssert.AreEqual(p1, r1);
                    CollectionAssert.AreEqual(p2, r2);
                }
            }
        }

        [Test]
        public async Task Invocation_DefinedDictionaries()
        {
            int size = 100;
            await TestAsync((prx, p1, p2) => prx.OpMyEnumDictAsync(p1, p2),
                            Enumerable.Range(0, size).ToDictionary(i => $"key-{i}", i => (MyEnum)(i % 3)),
                            Enumerable.Range(0, size).ToDictionary(i => $"key-{i}", i => (MyEnum)(i % 3)));

            await TestAsync((prx, p1, p2) => prx.OpMyStructDictAsync(p1, p2),
                            Enumerable.Range(0, size).ToDictionary(i => $"key-{i}", i => new MyStruct(i, i + 1)),
                            Enumerable.Range(0, size).ToDictionary(i => $"key-{i}", i => new MyStruct(i, i + 1)));

            await TestAsync((prx, p1, p2) => prx.OpAnotherStructDictAsync(p1, p2),
                            Enumerable.Range(0, size).ToDictionary(
                                i => $"key-{i}",
                                i =>
                                {
                                    return new AnotherStruct($"hello-{i}",
                                                             IInvocationServicePrx.Parse($"foo-{i}", _communicator),
                                                             (MyEnum)(i % 3),
                                                             new MyStruct(i, i + 1));
                                }),
                            Enumerable.Range(0, size).ToDictionary(
                                i => $"key-{i}",
                                i =>
                                {
                                    return new AnotherStruct($"hello-{i}",
                                                             IInvocationServicePrx.Parse($"foo-{i}", _communicator),
                                                             (MyEnum)(i % 3),
                                                             new MyStruct(i, i + 1));
                                }));

            async Task TestAsync<T>(
                Func<IInvocationServicePrx, Dictionary<string, T>, Dictionary<string, T>, Task<(Dictionary<string, T>, Dictionary<string, T>)>> invoker,
                Dictionary<string, T> p1,
                Dictionary<string, T> p2)
            {
                foreach (IInvocationServicePrx prx in new IInvocationServicePrx[] { _prx, _derivedPrx })
                {
                    (Dictionary<string, T> r1, Dictionary<string, T> r2) = await invoker(prx, p1, p2);
                    CollectionAssert.AreEqual(p1, r1);
                    CollectionAssert.AreEqual(p2, r2);
                }
            }
        }

        [Test]
        public async Task Invocation_MarshalledResult()
        {
            await Test1Async((prx, p1) => prx.OpAnotherStruct1Async(p1),
                            new AnotherStruct("hello",
                                              IInvocationServicePrx.Parse("foo", _communicator),
                                              MyEnum.enum1,
                                              new MyStruct(1, 2)));

            await Test1Async((prx, p1) => prx.OpStringSeq1Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToArray());

            await Test1Async((prx, p1) => prx.OpStringDict1Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToDictionary(key => key,
                                                                                            value => value));

            await Test2Async((prx, p1) => prx.OpAnotherStruct2Async(p1),
                            new AnotherStruct("hello",
                                              IInvocationServicePrx.Parse("foo", _communicator),
                                              MyEnum.enum1,
                                              new MyStruct(1, 2)));

            await Test2Async((prx, p1) => prx.OpStringSeq2Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToArray());

            await Test2Async((prx, p1) => prx.OpStringDict2Async(p1),
                            Enumerable.Range(0, 100).Select(i => $"hello-{i}").ToDictionary(key => key,
                                                                                            value => value));


            async Task Test1Async<T>(Func<IInvocationServicePrx, T, Task<T>> invoker, T p1)
            {
                foreach (IInvocationServicePrx prx in new IInvocationServicePrx[] { _prx, _derivedPrx })
                {
                    T r1 = await invoker(prx, p1);
                    Assert.AreEqual(p1, r1);
                }
            }

            async Task Test2Async<T>(Func<IInvocationServicePrx, T, Task<(T, T)>> invoker, T p1)
            {
                foreach (IInvocationServicePrx prx in new IInvocationServicePrx[] { _prx, _derivedPrx })
                {
                    (T r1, T r2) = await invoker(prx, p1);
                    Assert.AreEqual(p1, r1);
                    Assert.AreEqual(p1, r2);
                }
            }
        }

        [Test]
        public async Task Invocation_Oneway()
        {
            Assert.IsFalse(_prx.IsOneway);
            Assert.ThrowsAsync<SomeException>(async () => await _prx.OpOnewayAsync());
            await _prx.Clone(oneway: true).OpOnewayAsync();

            // This is invoked as a oneway, despite using a twoway proxy.
            await _prx.OpOnewayMetadataAsync();
            await _prx.Clone(oneway: true).OpOnewayMetadataAsync();
        }

        public class InvocationService : IAsyncInvocationService
        {
            // Builtin types
            public ValueTask<(byte, byte)> OpByteAsync(
                byte p1,
                byte p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(bool, bool)> OpBoolAsync(
                bool p1,
                bool p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(short, short)> OpShortAsync(
                short p1,
                short p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ushort, ushort)> OpUShortAsync(
                ushort p1,
                ushort p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(int, int)> OpIntAsync(
                int p1,
                int p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(int, int)> OpVarIntAsync(
                int p1,
                int p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(uint, uint)> OpUIntAsync(
                uint p1,
                uint p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(uint, uint)> OpVarUIntAsync(
                uint p1,
                uint p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(long, long)> OpLongAsync(
                long p1,
                long p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(long, long)> OpVarLongAsync(
                long p1,
                long p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ulong, ulong)> OpULongAsync(
                ulong p1,
                ulong p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ulong, ulong)> OpVarULongAsync(
                ulong p1,
                ulong p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(float, float)> OpFloatAsync(
                float p1,
                float p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(double, double)> OpDoubleAsync(
                double p1,
                double p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(string, string)> OpStringAsync(
                string p1,
                string p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

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

            // Builtin type dictionaries

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

            // Defined types
            public ValueTask<(MyEnum R1, MyEnum R2)> OpMyEnumAsync(
                MyEnum p1,
                MyEnum p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

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

            // Defined type sequences

            public ValueTask<(IEnumerable<MyEnum> R1, IEnumerable<MyEnum> R2)> OpMyEnumSeqAsync(
                MyEnum[] p1,
                MyEnum[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyStruct> R1, IEnumerable<MyStruct> R2)> OpMyStructSeqAsync(
                MyStruct[] p1,
                MyStruct[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<AnotherStruct> R1, IEnumerable<AnotherStruct> R2)> OpAnotherStructSeqAsync(
                AnotherStruct[] p1,
                AnotherStruct[] p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            // Defined type dictionaries

            public ValueTask<(IReadOnlyDictionary<string, MyEnum> R1, IReadOnlyDictionary<string, MyEnum> R2)> OpMyEnumDictAsync(
                Dictionary<string, MyEnum> p1,
                Dictionary<string, MyEnum> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<string, MyStruct> R1, IReadOnlyDictionary<string, MyStruct> R2)> OpMyStructDictAsync(
                Dictionary<string, MyStruct> p1,
                Dictionary<string, MyStruct> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IReadOnlyDictionary<string, AnotherStruct> R1, IReadOnlyDictionary<string, AnotherStruct> R2)> OpAnotherStructDictAsync(
                Dictionary<string, AnotherStruct> p1,
                Dictionary<string, AnotherStruct> p2,
                Current current,
                CancellationToken cancel) => new((p1, p2));

            // Marshalled result

            public ValueTask<IInvocationService.OpAnotherStruct1MarshaledReturnValue> OpAnotherStruct1Async(
                AnotherStruct p1,
                Current current,
                CancellationToken cancel) =>
                new(new IInvocationService.OpAnotherStruct1MarshaledReturnValue(p1, current));

            public ValueTask<IInvocationService.OpAnotherStruct2MarshaledReturnValue> OpAnotherStruct2Async(
                AnotherStruct p1,
                Current current,
                CancellationToken cancel) =>
                new(new IInvocationService.OpAnotherStruct2MarshaledReturnValue(p1, p1, current));

            public ValueTask<IInvocationService.OpStringSeq1MarshaledReturnValue> OpStringSeq1Async(
                string[] p1,
                Current current,
                CancellationToken cancel) =>
                new(new IInvocationService.OpStringSeq1MarshaledReturnValue(p1, current));

            public ValueTask<IInvocationService.OpStringSeq2MarshaledReturnValue> OpStringSeq2Async(
                string[] p1,
                Current current,
                CancellationToken cancel) =>
                new(new IInvocationService.OpStringSeq2MarshaledReturnValue(p1, p1, current));

            public ValueTask<IInvocationService.OpStringDict1MarshaledReturnValue> OpStringDict1Async(
                Dictionary<string, string> p1,
                Current current,
                CancellationToken cancel) =>
                new(new IInvocationService.OpStringDict1MarshaledReturnValue(p1, current));

            public ValueTask<IInvocationService.OpStringDict2MarshaledReturnValue> OpStringDict2Async(
                Dictionary<string, string> p1,
                Current current,
                CancellationToken cancel) =>
                new(new IInvocationService.OpStringDict2MarshaledReturnValue(p1, p1, current));

            // Oneway Operations

            public ValueTask OpOnewayAsync(Current current, CancellationToken cancel) => throw new SomeException();

            public ValueTask OpOnewayMetadataAsync(Current current, CancellationToken cancel) => throw new SomeException();
        }
    }
}
