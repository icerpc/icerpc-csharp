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
    public class SequenceTests
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly ISequenceOperationsPrx _prx;

        public SequenceTests(Protocol protocol)
        {
            _server = new Server
            {
                Dispatcher = new SequenceOperations(),
                Endpoint = TestHelper.GetUniqueColocEndpoint(protocol)
            };
            _server.Listen();
            _connection = new Connection { RemoteEndpoint = _server.ProxyEndpoint };
            _prx = ISequenceOperationsPrx.FromConnection(_connection);
            Assert.AreEqual(protocol, _prx.Protocol);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _connection.ShutdownAsync();
        }

        [Test]
        public async Task Sequence_BuiltinTypesAsync()
        {
            int size = 100;
            await TestSeqAsync((p1, p2) => _prx.OpByteSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (byte)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (byte)i).ToArray());
            await TestListAsync((p1, p2) => _prx.OpByteListAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => (byte)i).ToList(),
                                Enumerable.Range(0, size).Select(i => (byte)i).ToList());
            await TestLinkedListAsync(
                (p1, p2) => _prx.OpByteLinkedListAsync(p1, p2),
                new LinkedList<byte>(Enumerable.Range(0, size).Select(i => (byte)i)),
                new LinkedList<byte>(Enumerable.Range(0, size).Select(i => (byte)i).ToList()));

            await TestQueueAsync(
                (p1, p2) => _prx.OpByteQueueAsync(p1, p2),
                new Queue<byte>(Enumerable.Range(0, size).Select(i => (byte)i)),
                new Queue<byte>(Enumerable.Range(0, size).Select(i => (byte)i).ToList()));

            await TestStackAsync(
                (p1, p2) => _prx.OpByteStackAsync(p1, p2),
                new Stack<byte>(Enumerable.Range(0, size).Select(i => (byte)i)),
                new Stack<byte>(Enumerable.Range(0, size).Select(i => (byte)i).ToList()));

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpByteCustomSeqAsync(p1, p2),
                new Custom<byte>(Enumerable.Range(0, size).Select(i => (byte)i)),
                new Custom<byte>(Enumerable.Range(0, size).Select(i => (byte)i).ToList()));

            await TestSeqAsync((p1, p2) => _prx.OpBoolSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => i % 2 == 0).ToArray(),
                               Enumerable.Range(0, size).Select(i => i % 2 == 0).ToArray());
            await TestListAsync((p1, p2) => _prx.OpBoolListAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => i % 2 == 0).ToList(),
                                Enumerable.Range(0, size).Select(i => i % 2 == 0).ToList());
            await TestLinkedListAsync(
                (p1, p2) => _prx.OpBoolLinkedListAsync(p1, p2),
                new LinkedList<bool>(Enumerable.Range(0, size).Select(i => i % 2 == 0)),
                new LinkedList<bool>(Enumerable.Range(0, size).Select(i => i % 2 == 0).ToList()));

            await TestQueueAsync(
                (p1, p2) => _prx.OpBoolQueueAsync(p1, p2),
                new Queue<bool>(Enumerable.Range(0, size).Select(i => i % 2 == 0)),
                new Queue<bool>(Enumerable.Range(0, size).Select(i => i % 2 == 0).ToList()));

            await TestStackAsync(
                (p1, p2) => _prx.OpBoolStackAsync(p1, p2),
                new Stack<bool>(Enumerable.Range(0, size).Select(i => i % 2 == 0)),
                new Stack<bool>(Enumerable.Range(0, size).Select(i => i % 2 == 0).ToList()));

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpBoolCustomSeqAsync(p1, p2),
                new Custom<bool>(Enumerable.Range(0, size).Select(i => i % 2 == 0)),
                new Custom<bool>(Enumerable.Range(0, size).Select(i => i % 2 == 0).ToList()));

            await TestSeqAsync((p1, p2) => _prx.OpShortSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (short)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (short)i).ToArray());

            await TestSeqAsync((p1, p2) => _prx.OpUShortSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (ushort)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (ushort)i).ToArray());

            await TestSeqAsync((p1, p2) => _prx.OpIntSeqAsync(p1, p2),
                               Enumerable.Range(0, size).ToArray(),
                               Enumerable.Range(0, size).ToArray());

            await TestEnumerableSeqAsync((p1, p2) => _prx.OpVarIntSeqAsync(p1, p2),
                                         Enumerable.Range(0, size).ToArray(),
                                         Enumerable.Range(0, size).ToArray());

            await TestListAsync((p1, p2) => _prx.OpIntListAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => i).ToList(),
                                Enumerable.Range(0, size).Select(i => i).ToList());

            await TestLinkedListAsync(
                (p1, p2) => _prx.OpIntLinkedListAsync(p1, p2),
                new LinkedList<int>(Enumerable.Range(0, size).Select(i => i)),
                new LinkedList<int>(Enumerable.Range(0, size).Select(i => i).ToList()));

            await TestQueueAsync(
                (p1, p2) => _prx.OpIntQueueAsync(p1, p2),
                new Queue<int>(Enumerable.Range(0, size).Select(i => i)),
                new Queue<int>(Enumerable.Range(0, size).Select(i => i).ToList()));

            await TestStackAsync(
                (p1, p2) => _prx.OpIntStackAsync(p1, p2),
                new Stack<int>(Enumerable.Range(0, size).Select(i => i)),
                new Stack<int>(Enumerable.Range(0, size).Select(i => i).ToList()));

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpIntCustomSeqAsync(p1, p2),
                new Custom<int>(Enumerable.Range(0, size).Select(i => i)),
                new Custom<int>(Enumerable.Range(0, size).Select(i => i).ToList()));

            await TestSeqAsync((p1, p2) => _prx.OpUIntSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (uint)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (uint)i).ToArray());
            await TestEnumerableSeqAsync((p1, p2) => _prx.OpVarUIntSeqAsync(p1, p2),
                                         Enumerable.Range(0, size).Select(i => (uint)i).ToArray(),
                                         Enumerable.Range(0, size).Select(i => (uint)i).ToArray());

            await TestSeqAsync((p1, p2) => _prx.OpLongSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (long)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (long)i).ToArray());

            await TestListAsync((p1, p2) => _prx.OpLongListAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => (long)i).ToList(),
                                Enumerable.Range(0, size).Select(i => (long)i).ToList());

            await TestLinkedListAsync(
                (p1, p2) => _prx.OpLongLinkedListAsync(p1, p2),
                new LinkedList<long>(Enumerable.Range(0, size).Select(i => (long)i)),
                new LinkedList<long>(Enumerable.Range(0, size).Select(i => (long)i).ToList()));

            await TestQueueAsync(
                (p1, p2) => _prx.OpLongQueueAsync(p1, p2),
                new Queue<long>(Enumerable.Range(0, size).Select(i => (long)i)),
                new Queue<long>(Enumerable.Range(0, size).Select(i => (long)i).ToList()));

            await TestStackAsync(
                (p1, p2) => _prx.OpLongStackAsync(p1, p2),
                new Stack<long>(Enumerable.Range(0, size).Select(i => (long)i)),
                new Stack<long>(Enumerable.Range(0, size).Select(i => (long)i).ToList()));

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpLongCustomSeqAsync(p1, p2),
                new Custom<long>(Enumerable.Range(0, size).Select(i => (long)i)),
                new Custom<long>(Enumerable.Range(0, size).Select(i => (long)i).ToList()));

            await TestEnumerableSeqAsync((p1, p2) => _prx.OpVarLongSeqAsync(p1, p2),
                                         Enumerable.Range(0, size).Select(i => (long)i).ToArray(),
                                         Enumerable.Range(0, size).Select(i => (long)i).ToArray());

            await TestSeqAsync((p1, p2) => _prx.OpULongSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (ulong)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (ulong)i).ToArray());

            await TestEnumerableSeqAsync((p1, p2) => _prx.OpVarULongSeqAsync(p1, p2),
                                         Enumerable.Range(0, size).Select(i => (ulong)i).ToArray(),
                                         Enumerable.Range(0, size).Select(i => (ulong)i).ToArray());

            await TestSeqAsync((p1, p2) => _prx.OpFloatSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (float)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (float)i).ToArray());

            await TestListAsync((p1, p2) => _prx.OpFloatListAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => (float)i).ToList(),
                                Enumerable.Range(0, size).Select(i => (float)i).ToList());

            await TestLinkedListAsync(
                (p1, p2) => _prx.OpFloatLinkedListAsync(p1, p2),
                new LinkedList<float>(Enumerable.Range(0, size).Select(i => (float)i)),
                new LinkedList<float>(Enumerable.Range(0, size).Select(i => (float)i).ToList()));

            await TestQueueAsync(
                (p1, p2) => _prx.OpFloatQueueAsync(p1, p2),
                new Queue<float>(Enumerable.Range(0, size).Select(i => (float)i)),
                new Queue<float>(Enumerable.Range(0, size).Select(i => (float)i).ToList()));

            await TestStackAsync(
                (p1, p2) => _prx.OpFloatStackAsync(p1, p2),
                new Stack<float>(Enumerable.Range(0, size).Select(i => (float)i)),
                new Stack<float>(Enumerable.Range(0, size).Select(i => (float)i).ToList()));

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpFloatCustomSeqAsync(p1, p2),
                new Custom<float>(Enumerable.Range(0, size).Select(i => (float)i)),
                new Custom<float>(Enumerable.Range(0, size).Select(i => (float)i).ToList()));

            await TestSeqAsync((p1, p2) => _prx.OpDoubleSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (double)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (double)i).ToArray());

            await TestEnumerableSeqAsync((p1, p2) => _prx.OpStringSeqAsync(p1, p2),
                                         Enumerable.Range(0, size).Select(i => $"hello-{i}").ToArray(),
                                         Enumerable.Range(0, size).Select(i => $"hello-{i}").ToArray());
        }

        [Test]
        public async Task Sequence_DefinedTypesAsync()
        {
            int size = 100;
            Array myEnumValues = Enum.GetValues(typeof(MyEnum));

            await TestEnumerableSeqAsync(
                (p1, p2) => _prx.OpMyEnumSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i)).ToArray(),
                Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i)).ToArray());

            await TestListAsync(
                (p1, p2) => _prx.OpMyEnumListAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i)).ToList(),
                Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i)).ToList());

            await TestLinkedListAsync(
                (p1, p2) => _prx.OpMyEnumLinkedListAsync(p1, p2),
                new LinkedList<MyEnum>(Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i))),
                new LinkedList<MyEnum>(Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i))));

            await TestQueueAsync(
                (p1, p2) => _prx.OpMyEnumQueueAsync(p1, p2),
                new Queue<MyEnum>(Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i))),
                new Queue<MyEnum>(Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i))));

            await TestStackAsync(
                (p1, p2) => _prx.OpMyEnumStackAsync(p1, p2),
                new Stack<MyEnum>(Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i))),
                new Stack<MyEnum>(Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i))));

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpMyEnumCustomSeqAsync(p1, p2),
                new Custom<MyEnum>(Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i))),
                new Custom<MyEnum>(Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i))));

            Array myFixedLengthEnumValues = Enum.GetValues(typeof(MyFixedLengthEnum));
            await TestReadOnlyMemorySeqAsync(
                (p1, p2) => _prx.OpMyFixedLengthEnumSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(
                    i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i)).ToArray(),
                Enumerable.Range(0, size).Select(
                    i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i)).ToArray());

            await TestListAsync(
                (p1, p2) => _prx.OpMyFixedLengthEnumListAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i)).ToList(),
                Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i)).ToList());

            await TestLinkedListAsync(
                (p1, p2) => _prx.OpMyFixedLengthEnumLinkedListAsync(p1, p2),
                new LinkedList<MyFixedLengthEnum>(
                    Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))),
                new LinkedList<MyFixedLengthEnum>(
                    Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))));

            await TestQueueAsync(
                (p1, p2) => _prx.OpMyFixedLengthEnumQueueAsync(p1, p2),
                new Queue<MyFixedLengthEnum>(
                    Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))),
                new Queue<MyFixedLengthEnum>(
                    Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))));
            await TestStackAsync(
                (p1, p2) => _prx.OpMyFixedLengthEnumStackAsync(p1, p2),
                new Stack<MyFixedLengthEnum>(
                    Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))),
                new Stack<MyFixedLengthEnum>(
                    Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))));
            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpMyFixedLengthEnumCustomSeqAsync(p1, p2),
                new Custom<MyFixedLengthEnum>(
                    Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))),
                new Custom<MyFixedLengthEnum>(
                    Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))));

            await TestReadOnlyMemorySeqAsync(
                (p1, p2) => _prx.OpMyUncheckedEnumSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i).ToArray(),
                Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i).ToArray());

            await TestListAsync(
                (p1, p2) => _prx.OpMyUncheckedEnumListAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i).ToList(),
                Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i).ToList());

            await TestLinkedListAsync(
                (p1, p2) => _prx.OpMyUncheckedEnumLinkedListAsync(p1, p2),
                new LinkedList<MyUncheckedEnum>(
                    Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i)),
                new LinkedList<MyUncheckedEnum>(
                    Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i)));

            await TestQueueAsync(
                (p1, p2) => _prx.OpMyUncheckedEnumQueueAsync(p1, p2),
                new Queue<MyUncheckedEnum>(
                    Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i)),
                new Queue<MyUncheckedEnum>(
                    Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i)));

            await TestStackAsync(
                (p1, p2) => _prx.OpMyUncheckedEnumStackAsync(p1, p2),
                new Stack<MyUncheckedEnum>(
                    Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i)),
                new Stack<MyUncheckedEnum>(
                    Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i)));

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpMyUncheckedEnumCustomSeqAsync(p1, p2),
                new Custom<MyUncheckedEnum>(
                    Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i)),
                new Custom<MyUncheckedEnum>(
                    Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i)));

            await TestEnumerableSeqAsync(
                (p1, p2) => _prx.OpMyStructSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1)).ToArray(),
                Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1)).ToArray());

            await TestListAsync(
                (p1, p2) => _prx.OpMyStructListAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1)).ToList(),
                Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1)).ToList());

            await TestLinkedListAsync(
                (p1, p2) => _prx.OpMyStructLinkedListAsync(p1, p2),
                new LinkedList<MyStruct>(Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1))),
                new LinkedList<MyStruct>(Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1))));

            await TestQueueAsync(
                (p1, p2) => _prx.OpMyStructQueueAsync(p1, p2),
                new Queue<MyStruct>(Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1))),
                new Queue<MyStruct>(Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1))));
            await TestStackAsync(
                (p1, p2) => _prx.OpMyStructStackAsync(p1, p2),
                new Stack<MyStruct>(Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1))),
                new Stack<MyStruct>(Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1))));

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpMyStructCustomSeqAsync(p1, p2),
                new Custom<MyStruct>(Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1))),
                new Custom<MyStruct>(Enumerable.Range(0, size).Select(i => new MyStruct(i, i + 1))));

            await TestEnumerableSeqAsync(
                (p1, p2) => _prx.OpAnotherStructSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetAnotherStruct(i)).ToArray(),
                Enumerable.Range(0, size).Select(i => GetAnotherStruct(i)).ToArray());

            await TestListAsync(
                (p1, p2) => _prx.OpAnotherStructListAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetAnotherStruct(i)).ToList(),
                Enumerable.Range(0, size).Select(i => GetAnotherStruct(i)).ToList());

            await TestLinkedListAsync(
                (p1, p2) => _prx.OpAnotherStructLinkedListAsync(p1, p2),
                new LinkedList<AnotherStruct>(Enumerable.Range(0, size).Select(i => GetAnotherStruct(i))),
                new LinkedList<AnotherStruct>(Enumerable.Range(0, size).Select(i => GetAnotherStruct(i))));

            await TestQueueAsync(
                (p1, p2) => _prx.OpAnotherStructQueueAsync(p1, p2),
                new Queue<AnotherStruct>(Enumerable.Range(0, size).Select(i => GetAnotherStruct(i))),
                new Queue<AnotherStruct>(Enumerable.Range(0, size).Select(i => GetAnotherStruct(i))));

            await TestStackAsync(
                (p1, p2) => _prx.OpAnotherStructStackAsync(p1, p2),
                new Stack<AnotherStruct>(Enumerable.Range(0, size).Select(i => GetAnotherStruct(i))),
                new Stack<AnotherStruct>(Enumerable.Range(0, size).Select(i => GetAnotherStruct(i))));

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpAnotherStructCustomSeqAsync(p1, p2),
                new Custom<AnotherStruct>(Enumerable.Range(0, size).Select(i => GetAnotherStruct(i))),
                new Custom<AnotherStruct>(Enumerable.Range(0, size).Select(i => GetAnotherStruct(i))));

            await TestEnumerableSeqAsync(
                (p1, p2) => _prx.OpOperationsSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetOperationsPrx(i)).ToArray(),
                Enumerable.Range(0, size).Select(i => GetOperationsPrx(i)).ToArray());

            await TestListAsync(
                (p1, p2) => _prx.OpOperationsListAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetOperationsPrx(i)).ToList(),
                Enumerable.Range(0, size).Select(i => GetOperationsPrx(i)).ToList());

            await TestLinkedListAsync(
                (p1, p2) => _prx.OpOperationsLinkedListAsync(p1, p2),
                new LinkedList<IOperationsPrx>(Enumerable.Range(0, size).Select(i => GetOperationsPrx(i))),
                new LinkedList<IOperationsPrx>(Enumerable.Range(0, size).Select(i => GetOperationsPrx(i))));

            await TestQueueAsync(
                (p1, p2) => _prx.OpOperationsQueueAsync(p1, p2),
                new Queue<IOperationsPrx>(Enumerable.Range(0, size).Select(i => GetOperationsPrx(i))),
                new Queue<IOperationsPrx>(Enumerable.Range(0, size).Select(i => GetOperationsPrx(i))));

            await TestStackAsync(
                (p1, p2) => _prx.OpOperationsStackAsync(p1, p2),
                new Stack<IOperationsPrx>(Enumerable.Range(0, size).Select(i => GetOperationsPrx(i))),
                new Stack<IOperationsPrx>(Enumerable.Range(0, size).Select(i => GetOperationsPrx(i))));

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpOperationsCustomSeqAsync(p1, p2),
                new Custom<IOperationsPrx>(Enumerable.Range(0, size).Select(i => GetOperationsPrx(i))),
                new Custom<IOperationsPrx>(Enumerable.Range(0, size).Select(i => GetOperationsPrx(i))));

            IOperationsPrx GetOperationsPrx(int i) => IOperationsPrx.Parse($"ice+tcp://host/foo-{i}");

            AnotherStruct GetAnotherStruct(int i)
            {
                return new AnotherStruct($"hello-{i}",
                                         GetOperationsPrx(i),
                                         (MyEnum)myEnumValues.GetValue(i % myEnumValues.Length)!,
                                         new MyStruct(i, i + 1));
            }

            static T GetEnum<T>(Array values, int i) => (T)values.GetValue(i % values.Length)!;
        }

        public class SequenceOperations : ISequenceOperations
        {
            // Builtin type sequences

            public ValueTask<(ReadOnlyMemory<byte> R1, ReadOnlyMemory<byte> R2)> OpByteSeqAsync(
                byte[] p1,
                byte[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<bool> R1, ReadOnlyMemory<bool> R2)> OpBoolSeqAsync(
                bool[] p1,
                bool[] p2,
                Dispatch dispatch, CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<short> R1, ReadOnlyMemory<short> R2)> OpShortSeqAsync(
                short[] p1,
                short[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<ushort> R1, ReadOnlyMemory<ushort> R2)> OpUShortSeqAsync(
                ushort[] p1,
                ushort[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<int> R1, ReadOnlyMemory<int> R2)> OpIntSeqAsync(
                int[] p1,
                int[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<int> R1, IEnumerable<int> R2)> OpVarIntSeqAsync(
                int[] p1,
                int[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<uint> R1, ReadOnlyMemory<uint> R2)> OpUIntSeqAsync(
                uint[] p1,
                uint[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<uint> R1, IEnumerable<uint> R2)> OpVarUIntSeqAsync(
                uint[] p1,
                uint[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<long> R1, ReadOnlyMemory<long> R2)> OpLongSeqAsync(
                long[] p1,
                long[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<long> R1, IEnumerable<long> R2)> OpVarLongSeqAsync(
                long[] p1,
                long[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<ulong> R1, ReadOnlyMemory<ulong> R2)> OpULongSeqAsync(
                ulong[] p1,
                ulong[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<ulong> R1, IEnumerable<ulong> R2)> OpVarULongSeqAsync(
                ulong[] p1,
                ulong[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<float> R1, ReadOnlyMemory<float> R2)> OpFloatSeqAsync(
                float[] p1,
                float[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<double> R1, ReadOnlyMemory<double> R2)> OpDoubleSeqAsync(
                double[] p1,
                double[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<string> R1, IEnumerable<string> R2)> OpStringSeqAsync(
                string[] p1,
                string[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            // Defined types sequences
            public ValueTask<(IEnumerable<MyEnum> R1, IEnumerable<MyEnum> R2)> OpMyEnumSeqAsync(
                MyEnum[] p1,
                MyEnum[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<MyFixedLengthEnum> R1, ReadOnlyMemory<MyFixedLengthEnum> R2)> OpMyFixedLengthEnumSeqAsync(
                MyFixedLengthEnum[] p1,
                MyFixedLengthEnum[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<MyUncheckedEnum> R1, ReadOnlyMemory<MyUncheckedEnum> R2)> OpMyUncheckedEnumSeqAsync(
                MyUncheckedEnum[] p1,
                MyUncheckedEnum[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyStruct> R1, IEnumerable<MyStruct> R2)> OpMyStructSeqAsync(
                MyStruct[] p1,
                MyStruct[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<IOperationsPrx> R1, IEnumerable<IOperationsPrx> R2)> OpOperationsSeqAsync(
                IOperationsPrx[] p1,
                IOperationsPrx[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<AnotherStruct> R1, IEnumerable<AnotherStruct> R2)> OpAnotherStructSeqAsync(
                AnotherStruct[] p1,
                AnotherStruct[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<byte> R1, IEnumerable<byte> R2)> OpByteListAsync(
                List<byte> p1,
                List<byte> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<byte> R1, IEnumerable<byte> R2)> OpByteLinkedListAsync(
                LinkedList<byte> p1,
                LinkedList<byte> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<byte> R1, IEnumerable<byte> R2)> OpByteQueueAsync(
                Queue<byte> p1,
                Queue<byte> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<byte> R1, IEnumerable<byte> R2)> OpByteStackAsync(
                Stack<byte> p1,
                Stack<byte> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<byte> R1, IEnumerable<byte> R2)> OpByteCustomSeqAsync(
                Custom<byte> p1,
                Custom<byte> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<bool> R1, IEnumerable<bool> R2)> OpBoolListAsync(
                List<bool> p1,
                List<bool> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<bool> R1, IEnumerable<bool> R2)> OpBoolLinkedListAsync(
                LinkedList<bool> p1,
                LinkedList<bool> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<bool> R1, IEnumerable<bool> R2)> OpBoolQueueAsync(
                Queue<bool> p1,
                Queue<bool> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<bool> R1, IEnumerable<bool> R2)> OpBoolStackAsync(
                Stack<bool> p1,
                Stack<bool> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<bool> R1, IEnumerable<bool> R2)> OpBoolCustomSeqAsync(
                Custom<bool> p1,
                Custom<bool> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<int> R1, IEnumerable<int> R2)> OpIntListAsync(
                List<int> p1,
                List<int> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<int> R1, IEnumerable<int> R2)> OpIntLinkedListAsync(
                LinkedList<int> p1,
                LinkedList<int> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<int> R1, IEnumerable<int> R2)> OpIntQueueAsync(
                Queue<int> p1,
                Queue<int> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<int> R1, IEnumerable<int> R2)> OpIntStackAsync(
                Stack<int> p1,
                Stack<int> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<int> R1, IEnumerable<int> R2)> OpIntCustomSeqAsync(
                Custom<int> p1,
                Custom<int> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<long> R1, IEnumerable<long> R2)> OpLongListAsync(
                List<long> p1,
                List<long> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<long> R1, IEnumerable<long> R2)> OpLongLinkedListAsync(
                LinkedList<long> p1,
                LinkedList<long> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<long> R1, IEnumerable<long> R2)> OpLongQueueAsync(
                Queue<long> p1,
                Queue<long> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<long> R1, IEnumerable<long> R2)> OpLongStackAsync(
                Stack<long> p1,
                Stack<long> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<long> R1, IEnumerable<long> R2)> OpLongCustomSeqAsync(
                Custom<long> p1,
                Custom<long> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<float> R1, IEnumerable<float> R2)> OpFloatListAsync(
                List<float> p1,
                List<float> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<float> R1, IEnumerable<float> R2)> OpFloatLinkedListAsync(
                LinkedList<float> p1,
                LinkedList<float> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<float> R1, IEnumerable<float> R2)> OpFloatQueueAsync(
                Queue<float> p1,
                Queue<float> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<float> R1, IEnumerable<float> R2)> OpFloatStackAsync(
                Stack<float> p1,
                Stack<float> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<float> R1, IEnumerable<float> R2)> OpFloatCustomSeqAsync(
                Custom<float> p1,
                Custom<float> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<string> R1, IEnumerable<string> R2)> OpStringListAsync(
                List<string> p1,
                List<string> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<string> R1, IEnumerable<string> R2)> OpStringLinkedListAsync(
                LinkedList<string> p1,
                LinkedList<string> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<string> R1, IEnumerable<string> R2)> OpStringQueueAsync(
                Queue<string> p1,
                Queue<string> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<string> R1, IEnumerable<string> R2)> OpStringStackAsync(
                Stack<string> p1,
                Stack<string> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<string> R1, IEnumerable<string> R2)> OpStringCustomSeqAsync(
                Custom<string> p1,
                Custom<string> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyEnum> R1, IEnumerable<MyEnum> R2)> OpMyEnumListAsync(
                List<MyEnum> p1,
                List<MyEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyEnum> R1, IEnumerable<MyEnum> R2)> OpMyEnumLinkedListAsync(
                LinkedList<MyEnum> p1,
                LinkedList<MyEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyEnum> R1, IEnumerable<MyEnum> R2)> OpMyEnumQueueAsync(
                Queue<MyEnum> p1,
                Queue<MyEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyEnum> R1, IEnumerable<MyEnum> R2)> OpMyEnumStackAsync(
                Stack<MyEnum> p1,
                Stack<MyEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyEnum> R1, IEnumerable<MyEnum> R2)> OpMyEnumCustomSeqAsync(
                Custom<MyEnum> p1,
                Custom<MyEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyFixedLengthEnum> R1, IEnumerable<MyFixedLengthEnum> R2)> OpMyFixedLengthEnumListAsync(
                List<MyFixedLengthEnum> p1,
                List<MyFixedLengthEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyFixedLengthEnum> R1, IEnumerable<MyFixedLengthEnum> R2)> OpMyFixedLengthEnumLinkedListAsync(
                LinkedList<MyFixedLengthEnum> p1,
                LinkedList<MyFixedLengthEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyFixedLengthEnum> R1, IEnumerable<MyFixedLengthEnum> R2)> OpMyFixedLengthEnumQueueAsync(
                Queue<MyFixedLengthEnum> p1,
                Queue<MyFixedLengthEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyFixedLengthEnum> R1, IEnumerable<MyFixedLengthEnum> R2)> OpMyFixedLengthEnumStackAsync(
                Stack<MyFixedLengthEnum> p1,
                Stack<MyFixedLengthEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyFixedLengthEnum> R1, IEnumerable<MyFixedLengthEnum> R2)> OpMyFixedLengthEnumCustomSeqAsync(
                Custom<MyFixedLengthEnum> p1,
                Custom<MyFixedLengthEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyUncheckedEnum> R1, IEnumerable<MyUncheckedEnum> R2)> OpMyUncheckedEnumListAsync(
                List<MyUncheckedEnum> p1,
                List<MyUncheckedEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyUncheckedEnum> R1, IEnumerable<MyUncheckedEnum> R2)> OpMyUncheckedEnumLinkedListAsync(
                LinkedList<MyUncheckedEnum> p1,
                LinkedList<MyUncheckedEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyUncheckedEnum> R1, IEnumerable<MyUncheckedEnum> R2)> OpMyUncheckedEnumQueueAsync(
                Queue<MyUncheckedEnum> p1,
                Queue<MyUncheckedEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyUncheckedEnum> R1, IEnumerable<MyUncheckedEnum> R2)> OpMyUncheckedEnumStackAsync(
                Stack<MyUncheckedEnum> p1,
                Stack<MyUncheckedEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyUncheckedEnum> R1, IEnumerable<MyUncheckedEnum> R2)> OpMyUncheckedEnumCustomSeqAsync(
                Custom<MyUncheckedEnum> p1,
                Custom<MyUncheckedEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyStruct> R1, IEnumerable<MyStruct> R2)> OpMyStructListAsync(
                List<MyStruct> p1,
                List<MyStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyStruct> R1, IEnumerable<MyStruct> R2)> OpMyStructLinkedListAsync(
                LinkedList<MyStruct> p1,
                LinkedList<MyStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyStruct> R1, IEnumerable<MyStruct> R2)> OpMyStructQueueAsync(
                Queue<MyStruct> p1,
                Queue<MyStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyStruct> R1, IEnumerable<MyStruct> R2)> OpMyStructStackAsync(
                Stack<MyStruct> p1,
                Stack<MyStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyStruct> R1, IEnumerable<MyStruct> R2)> OpMyStructCustomSeqAsync(
                Custom<MyStruct> p1,
                Custom<MyStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<IOperationsPrx> R1, IEnumerable<IOperationsPrx> R2)> OpOperationsListAsync(
                List<IOperationsPrx> p1,
                List<IOperationsPrx> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<IOperationsPrx> R1, IEnumerable<IOperationsPrx> R2)> OpOperationsLinkedListAsync(
                LinkedList<IOperationsPrx> p1,
                LinkedList<IOperationsPrx> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<IOperationsPrx> R1, IEnumerable<IOperationsPrx> R2)> OpOperationsQueueAsync(
                Queue<IOperationsPrx> p1,
                Queue<IOperationsPrx> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<IOperationsPrx> R1, IEnumerable<IOperationsPrx> R2)> OpOperationsStackAsync(
                Stack<IOperationsPrx> p1,
                Stack<IOperationsPrx> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<IOperationsPrx> R1, IEnumerable<IOperationsPrx> R2)> OpOperationsCustomSeqAsync(
                Custom<IOperationsPrx> p1,
                Custom<IOperationsPrx> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<AnotherStruct> R1, IEnumerable<AnotherStruct> R2)> OpAnotherStructListAsync(
                List<AnotherStruct> p1,
                List<AnotherStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<AnotherStruct> R1, IEnumerable<AnotherStruct> R2)> OpAnotherStructLinkedListAsync(
                LinkedList<AnotherStruct> p1,
                LinkedList<AnotherStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<AnotherStruct> R1, IEnumerable<AnotherStruct> R2)> OpAnotherStructQueueAsync(
                Queue<AnotherStruct> p1,
                Queue<AnotherStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<AnotherStruct> R1, IEnumerable<AnotherStruct> R2)> OpAnotherStructStackAsync(
                Stack<AnotherStruct> p1,
                Stack<AnotherStruct> p2,
                Dispatch dispatch, CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<AnotherStruct> R1, IEnumerable<AnotherStruct> R2)> OpAnotherStructCustomSeqAsync(
                Custom<AnotherStruct> p1,
                Custom<AnotherStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));
        }

        private static async Task TestSeqAsync<T>(
                Func<ReadOnlyMemory<T>, ReadOnlyMemory<T>, Task<(T[], T[])>> invoker,
                T[] p1,
                T[] p2)
        {
            (T[] r1, T[] r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(r1, p1);
            CollectionAssert.AreEqual(r2, p2);
        }

        private static async Task TestEnumerableSeqAsync<T>(
            Func<IEnumerable<T>, IEnumerable<T>, Task<(T[], T[])>> invoker,
            T[] p1,
            T[] p2)
        {
            (T[] r1, T[] r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(r1, p1);
            CollectionAssert.AreEqual(r2, p2);
        }

        private static async Task TestReadOnlyMemorySeqAsync<T>(
            Func<ReadOnlyMemory<T>, ReadOnlyMemory<T>, Task<(T[], T[])>> invoker,
            T[] p1,
            T[] p2)
        {
            (T[] r1, T[] r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(p1, r1);
            CollectionAssert.AreEqual(p2, r2);
        }

        private static async Task TestListAsync<T>(
                Func<List<T>, List<T>, Task<(List<T>, List<T>)>> invoker,
                List<T> p1,
                List<T> p2)
        {
            (List<T> r1, List<T> r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(r1, p1);
            CollectionAssert.AreEqual(r2, p2);
        }

        private static async Task TestLinkedListAsync<T>(
            Func<LinkedList<T>, LinkedList<T>, Task<(LinkedList<T>, LinkedList<T>)>> invoker,
            LinkedList<T> p1,
            LinkedList<T> p2)
        {
            (LinkedList<T> r1, LinkedList<T> r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(r1, p1);
            CollectionAssert.AreEqual(r2, p2);
        }

        private static async Task TestQueueAsync<T>(
            Func<Queue<T>, Queue<T>, Task<(Queue<T>, Queue<T>)>> invoker,
            Queue<T> p1,
            Queue<T> p2)
        {
            (Queue<T> r1, Queue<T> r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(r1, p1);
            CollectionAssert.AreEqual(r2, p2);
        }

        private static async Task TestStackAsync<T>(
            Func<Stack<T>, Stack<T>, Task<(Stack<T>, Stack<T>)>> invoker,
            Stack<T> p1,
            Stack<T> p2)
        {
            (Stack<T> r1, Stack<T> r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(r1, p1);
            CollectionAssert.AreEqual(r2, p2);
        }

        private static async Task TestCustomSeqAsync<T>(
            Func<Custom<T>, Custom<T>, Task<(Custom<T>, Custom<T>)>> invoker,
            Custom<T> p1,
            Custom<T> p2)
        {
            (Custom<T> r1, Custom<T> r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(r1, p1);
            CollectionAssert.AreEqual(r2, p2);
        }
    }
}
