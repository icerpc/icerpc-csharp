// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Operations
{
    public sealed class AsyncMyDerivedClass : IAsyncMyDerivedClass
    {
        private readonly object _mutex = new();
        private int _opByteSOnewayCallCount;
        private Thread_opVoid? _opVoidThread;

        private readonly List<Stream> _streams = new();
        internal class Thread_opVoid : TaskCompletionSource<object?>
        {
            private readonly object _mutex = new();

            public void Start()
            {
                lock (_mutex)
                {
                    _thread = new Thread(new ThreadStart(Run));
                    _thread.Start();
                }
            }

            public void Run() => SetResult(null);

            public void Join()
            {
                lock (_mutex)
                {
                    _thread!.Join();
                }
            }

            private Thread? _thread;
        }

        // Override the Object "pseudo" operations to verify the operation mode.
        public ValueTask<bool> IceIsAAsync(string id, Current current, CancellationToken cancel)
        {
            TestHelper.Assert(current.IsIdempotent);
            return new(typeof(IMyDerivedClass).GetAllIceTypeIds().Contains(id));
        }

        public ValueTask IcePingAsync(Current current, CancellationToken cancel)
        {
            TestHelper.Assert(current.IsIdempotent);
            return default;
        }

        public ValueTask<IEnumerable<string>> IceIdsAsync(Current current, CancellationToken cancel)
        {
            TestHelper.Assert(current.IsIdempotent);
            return new(typeof(IMyDerivedClass).GetAllIceTypeIds());
        }

        public ValueTask<string> IceIdAsync(Current current, CancellationToken cancel)
        {
            TestHelper.Assert(current.IsIdempotent);
            return new(typeof(IMyDerivedClass).GetIceTypeId()!);
        }

        public ValueTask ShutdownAsync(Current current, CancellationToken cancel)
        {
            while (_opVoidThread != null)
            {
                _opVoidThread.Join();
                _opVoidThread = null;
            }

            current.Server.ShutdownAsync();
            return default;
        }

        public ValueTask<bool> SupportsCompressAsync(Current current, CancellationToken cancel) =>
            new(true);

        public ValueTask OpVoidAsync(Current current, CancellationToken cancel)
        {
            TestHelper.Assert(!current.IsIdempotent);

            while (_opVoidThread != null)
            {
                _opVoidThread.Join();
                _opVoidThread = null;
            }

            _opVoidThread = new Thread_opVoid();
            _opVoidThread.Start();
            return new(_opVoidThread.Task);
        }

        public ValueTask<(bool, bool)> OpBoolAsync(bool p1, bool p2, Current current, CancellationToken cancel) =>
            new((p2, p1));

        public ValueTask<(ReadOnlyMemory<bool>, ReadOnlyMemory<bool>)> OpBoolSAsync(
            bool[] p1,
            bool[] p2,
            Current current,
            CancellationToken cancel)
        {
            bool[] p3 = new bool[p1.Length + p2.Length];
            Array.Copy(p1, p3, p1.Length);
            Array.Copy(p2, 0, p3, p1.Length, p2.Length);

            bool[] r = new bool[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                r[i] = p1[^(i + 1)];
            }

            return new((r, p3));
        }

        public ValueTask<(IEnumerable<IEnumerable<bool>>, IEnumerable<IEnumerable<bool>>)> OpBoolSSAsync(
            bool[][] p1,
            bool[][] p2,
            Current current,
            CancellationToken cancel)
        {
            bool[][] p3 = new bool[p1.Length + p2.Length][];
            Array.Copy(p1, p3, p1.Length);
            Array.Copy(p2, 0, p3, p1.Length, p2.Length);

            bool[][] r = new bool[p1.Length][];
            for (int i = 0; i < p1.Length; i++)
            {
                r[i] = p1[^(i + 1)];
            }

            return new((r, p3));
        }

        public ValueTask<(byte, byte)> OpByteAsync(byte p1, byte p2, Current current, CancellationToken cancel) =>
            new((p1, (byte)(p1 ^ p2)));

        public ValueTask<(IReadOnlyDictionary<byte, bool>, IReadOnlyDictionary<byte, bool>)> OpByteBoolDAsync(
            Dictionary<byte, bool> p1,
            Dictionary<byte, bool> p2,
            Current current,
            CancellationToken cancel) => new((MergeDictionaries(p1, p2), p1));

        public ValueTask<(ReadOnlyMemory<byte>, ReadOnlyMemory<byte>)> OpByteSAsync(
            byte[] p1,
            byte[] p2,
            Current current,
            CancellationToken cancel)
        {
            byte[] p3 = new byte[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                p3[i] = p1[^(i + 1)];
            }

            byte[] r = new byte[p1.Length + p2.Length];
            Array.Copy(p1, r, p1.Length);
            Array.Copy(p2, 0, r, p1.Length, p2.Length);

            return new((r, p3));
        }

        public ValueTask<(IEnumerable<IEnumerable<byte>>, IEnumerable<IEnumerable<byte>>)> OpByteSSAsync(
            byte[][] p1,
            byte[][] p2,
            Current current,
            CancellationToken cancel)
        {
            byte[][] p3 = new byte[p1.Length][];
            for (int i = 0; i < p1.Length; i++)
            {
                p3[i] = p1[^(i + 1)];
            }

            byte[][] r = new byte[p1.Length + p2.Length][];
            Array.Copy(p1, r, p1.Length);
            Array.Copy(p2, 0, r, p1.Length, p2.Length);

            return new((r, p3));
        }

        public ValueTask<(double, float, double)> OpFloatDoubleAsync(
            float p1,
            double p2,
            Current current,
            CancellationToken cancel) =>
            new((p2, p1, p2));

        public ValueTask<(ReadOnlyMemory<double>, ReadOnlyMemory<float>, ReadOnlyMemory<double>)> OpFloatDoubleSAsync(
            float[] p1,
            double[] p2,
            Current current,
            CancellationToken cancel)
        {
            float[] p3 = p1;

            double[] p4 = new double[p2.Length];
            for (int i = 0; i < p2.Length; i++)
            {
                p4[i] = p2[^(i + 1)];
            }

            double[] r = new double[p2.Length + p1.Length];
            Array.Copy(p2, r, p2.Length);
            for (int i = 0; i < p1.Length; i++)
            {
                r[p2.Length + i] = p1[i];
            }

            return new((r, p3, p4));
        }

        public ValueTask<(IEnumerable<IEnumerable<double>>, IEnumerable<IEnumerable<float>>, IEnumerable<IEnumerable<double>>)> OpFloatDoubleSSAsync(
            float[][] p1,
            double[][] p2,
            Current current,
            CancellationToken cancel)
        {
            float[][] p3 = p1;

            double[][] p4 = new double[p2.Length][];
            for (int i = 0; i < p2.Length; i++)
            {
                p4[i] = p2[^(i + 1)];
            }

            double[][] r = new double[p2.Length + p2.Length][];
            Array.Copy(p2, r, p2.Length);
            for (int i = 0; i < p2.Length; i++)
            {
                r[p2.Length + i] = new double[p2[i].Length];
                for (int j = 0; j < p2[i].Length; j++)
                {
                    r[p2.Length + i][j] = p2[i][j];
                }
            }

            return new((r, p3, p4));
        }

        public ValueTask<(IReadOnlyDictionary<long, float>, IReadOnlyDictionary<long, float>)> OpLongFloatDAsync(
            Dictionary<long, float> p1,
            Dictionary<long, float> p2,
            Current current,
            CancellationToken cancel) => new((MergeDictionaries(p1, p2), p1));

        public ValueTask<(IReadOnlyDictionary<ulong, float>, IReadOnlyDictionary<ulong, float>)> OpULongFloatDAsync(
            Dictionary<ulong, float> p1,
            Dictionary<ulong, float> p2,
            Current current,
            CancellationToken cancel) => new((MergeDictionaries(p1, p2), p1));

        public ValueTask<(IMyClassPrx?, IMyClassPrx?, IMyClassPrx?)> OpMyClassAsync(
            IMyClassPrx? p1,
            Current current,
            CancellationToken cancel) =>
            new((
                IMyClassPrx.Factory.Create(current.Server, current.Path),
                p1,
                IMyClassPrx.Factory.Create(current.Server, "noSuchIdentity")));

        public ValueTask<(MyEnum, MyEnum)> OpMyEnumAsync(MyEnum p1, Current current, CancellationToken cancel) =>
            new((MyEnum.enum3, p1));

        public ValueTask<(IReadOnlyDictionary<short, int>, IReadOnlyDictionary<short, int>)> OpShortIntDAsync(
            Dictionary<short, int> p1,
            Dictionary<short, int> p2,
            Current current,
            CancellationToken cancel) => new((MergeDictionaries(p1, p2), p1));

        public ValueTask<(IReadOnlyDictionary<ushort, uint>, IReadOnlyDictionary<ushort, uint>)> OpUShortUIntDAsync(
            Dictionary<ushort, uint> p1,
            Dictionary<ushort, uint> p2,
            Current current,
            CancellationToken cancel) => new((MergeDictionaries(p1, p2), p1));

        public ValueTask<(long, short, int, long)> OpShortIntLongAsync(
            short p1,
            int p2,
            long p3,
            Current current,
            CancellationToken cancel) =>
            new((p3, p1, p2, p3));

        public ValueTask<(ulong, ushort, uint, ulong)> OpUShortUIntULongAsync(
            ushort p1,
            uint p2,
            ulong p3,
            Current current,
            CancellationToken cancel) =>
            new((p3, p1, p2, p3));

        public ValueTask<int> OpVarIntAsync(int v, Current current, CancellationToken cancel) => new(v);
        public ValueTask<uint> OpVarUIntAsync(uint v, Current current, CancellationToken cancel) => new(v);
        public ValueTask<long> OpVarLongAsync(long v, Current current, CancellationToken cancel) => new(v);
        public ValueTask<ulong> OpVarULongAsync(ulong v, Current current, CancellationToken cancel) => new(v);

        public ValueTask<(ReadOnlyMemory<long>,
                          ReadOnlyMemory<short>,
                          ReadOnlyMemory<int>,
                          ReadOnlyMemory<long>)> OpShortIntLongSAsync(
            short[] p1,
            int[] p2,
            long[] p3,
            Current current,
            CancellationToken cancel)
        {
            short[] p4 = p1;
            int[] p5 = new int[p2.Length];
            for (int i = 0; i < p2.Length; i++)
            {
                p5[i] = p2[^(i + 1)];
            }
            long[]? p6 = new long[p3.Length + p3.Length];
            Array.Copy(p3, p6, p3.Length);
            Array.Copy(p3, 0, p6, p3.Length, p3.Length);

            return new((p3, p4, p5, p6));
        }

        public ValueTask<(ReadOnlyMemory<ulong>, ReadOnlyMemory<ushort>, ReadOnlyMemory<uint>, ReadOnlyMemory<ulong>)>
        OpUShortUIntULongSAsync(ushort[] p1, uint[] p2, ulong[] p3, Current current, CancellationToken cancel)
        {
            ushort[]? p4 = p1;
            uint[] p5 = new uint[p2.Length];
            for (int i = 0; i < p2.Length; i++)
            {
                p5[i] = p2[^(i + 1)];
            }
            ulong[] p6 = new ulong[p3.Length + p3.Length];
            Array.Copy(p3, p6, p3.Length);
            Array.Copy(p3, 0, p6, p3.Length, p3.Length);

            return new((p3, p4, p5, p6));
        }

        public ValueTask<(IEnumerable<long>, IEnumerable<int>, IEnumerable<long>)>
        OpVarIntVarLongSAsync(int[] p1, long[] p2, Current current, CancellationToken cancel)
        {
            int[] p4 = new int[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                p4[i] = p1[^(i + 1)];
            }

            long[] p5 = new long[p2.Length + p2.Length];
            Array.Copy(p2, p5, p2.Length);
            Array.Copy(p2, 0, p5, p2.Length, p2.Length);

            return new((p2, p4, p5));
        }

        public ValueTask<(IEnumerable<ulong>, IEnumerable<uint>, IEnumerable<ulong>)>
        OpVarUIntVarULongSAsync(uint[] p1, ulong[] p2, Current current, CancellationToken cancel)
        {
            uint[] p4 = new uint[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                p4[i] = p1[^(i + 1)];
            }

            ulong[] p5 = new ulong[p2.Length + p2.Length];
            Array.Copy(p2, p5, p2.Length);
            Array.Copy(p2, 0, p5, p2.Length, p2.Length);

            return new((p2, p4, p5));
        }

        public ValueTask<(IEnumerable<IEnumerable<long>>, IEnumerable<IEnumerable<short>>, IEnumerable<IEnumerable<int>>, IEnumerable<IEnumerable<long>>)>
        OpShortIntLongSSAsync(short[][] p1, int[][] p2, long[][] p3, Current current, CancellationToken cancel)
        {
            short[][] p4 = p1;

            int[][] p5 = new int[p2.Length][];
            for (int i = 0; i < p2.Length; i++)
            {
                p5[i] = p2[^(i + 1)];
            }

            long[][] p6 = new long[p3.Length + p3.Length][];
            Array.Copy(p3, p6, p3.Length);
            Array.Copy(p3, 0, p6, p3.Length, p3.Length);
            return new((p3, p4, p5, p6));
        }

        public ValueTask<(IEnumerable<IEnumerable<ulong>>, IEnumerable<IEnumerable<ushort>>, IEnumerable<IEnumerable<uint>>, IEnumerable<IEnumerable<ulong>>)>
        OpUShortUIntULongSSAsync(ushort[][] p1, uint[][] p2, ulong[][] p3, Current current, CancellationToken cancel)
        {
            ushort[][] p4 = p1;

            uint[][] p5 = new uint[p2.Length][];
            for (int i = 0; i < p2.Length; i++)
            {
                p5[i] = p2[^(i + 1)];
            }

            ulong[][]? p6 = new ulong[p3.Length + p3.Length][];
            Array.Copy(p3, p6, p3.Length);
            Array.Copy(p3, 0, p6, p3.Length, p3.Length);

            return new((p3, p4, p5, p6));
        }

        public ValueTask<(string, string)>
        OpStringAsync(string p1, string p2, Current current, CancellationToken cancel) =>
            new(($"{p1} {p2}", $"{p2} {p1}"));

        public ValueTask<(IReadOnlyDictionary<string, MyEnum>, IReadOnlyDictionary<string, MyEnum>)>
        OpStringMyEnumDAsync(
            Dictionary<string, MyEnum> p1,
            Dictionary<string, MyEnum> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p1));

        public ValueTask<(IReadOnlyDictionary<MyEnum, string>, IReadOnlyDictionary<MyEnum, string>)>
        OpMyEnumStringDAsync(
            Dictionary<MyEnum, string> p1,
            Dictionary<MyEnum, string> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p1));

        public ValueTask<(IReadOnlyDictionary<MyStruct, MyEnum>, IReadOnlyDictionary<MyStruct, MyEnum>)>
        OpMyStructMyEnumDAsync(
            Dictionary<MyStruct, MyEnum> p1,
            Dictionary<MyStruct, MyEnum> p2,
            Current current,
            CancellationToken cancel) => new((MergeDictionaries(p1, p2), p1));

        public ValueTask<(IEnumerable<IReadOnlyDictionary<byte, bool>>, IEnumerable<IReadOnlyDictionary<byte, bool>>)>
        OpByteBoolDSAsync(
            Dictionary<byte, bool>[] p1,
            Dictionary<byte, bool>[] p2,
            Current current,
            CancellationToken cancel)
        {
            var p3 = new Dictionary<byte, bool>[p1.Length + p2.Length];
            Array.Copy(p2, p3, p2.Length);
            Array.Copy(p1, 0, p3, p2.Length, p1.Length);

            var r = new Dictionary<byte, bool>[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                r[i] = p1[^(i + 1)];
            }
            return new((r, p3));
        }

        public ValueTask<(IEnumerable<IReadOnlyDictionary<short, int>>, IEnumerable<IReadOnlyDictionary<short, int>>)>
        OpShortIntDSAsync(
            Dictionary<short, int>[] p1,
            Dictionary<short, int>[] p2,
            Current current,
            CancellationToken cancel)
        {
            var p3 = new Dictionary<short, int>[p1.Length + p2.Length];
            Array.Copy(p2, p3, p2.Length);
            Array.Copy(p1, 0, p3, p2.Length, p1.Length);

            var r = new Dictionary<short, int>[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                r[i] = p1[^(i + 1)];
            }
            return new((r, p3));
        }

        public ValueTask<(IEnumerable<IReadOnlyDictionary<ushort, uint>>, IEnumerable<IReadOnlyDictionary<ushort, uint>>)>
        OpUShortUIntDSAsync(
            Dictionary<ushort, uint>[] p1,
            Dictionary<ushort, uint>[] p2,
            Current current,
            CancellationToken cancel)
        {
            var p3 = new Dictionary<ushort, uint>[p1.Length + p2.Length];
            Array.Copy(p2, p3, p2.Length);
            Array.Copy(p1, 0, p3, p2.Length, p1.Length);

            var r = new Dictionary<ushort, uint>[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                r[i] = p1[^(i + 1)];
            }
            return new((r, p3));
        }

        public ValueTask<(IEnumerable<IReadOnlyDictionary<long, float>>, IEnumerable<IReadOnlyDictionary<long, float>>)>
        OpLongFloatDSAsync(
            Dictionary<long, float>[] p1,
            Dictionary<long, float>[] p2,
            Current current,
            CancellationToken cancel)
        {
            var p3 = new Dictionary<long, float>[p1.Length + p2.Length];
            Array.Copy(p2, p3, p2.Length);
            Array.Copy(p1, 0, p3, p2.Length, p1.Length);

            var r = new Dictionary<long, float>[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                r[i] = p1[^(i + 1)];
            }
            return new((r, p3));
        }

        public ValueTask<(IEnumerable<IReadOnlyDictionary<ulong, float>>, IEnumerable<IReadOnlyDictionary<ulong, float>>)>
        OpULongFloatDSAsync(
            Dictionary<ulong, float>[] p1,
            Dictionary<ulong, float>[] p2,
            Current current,
            CancellationToken cancel)
        {
            var p3 = new Dictionary<ulong, float>[p1.Length + p2.Length];
            Array.Copy(p2, p3, p2.Length);
            Array.Copy(p1, 0, p3, p2.Length, p1.Length);

            var r = new Dictionary<ulong, float>[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                r[i] = p1[^(i + 1)];
            }
            return new((r, p3));
        }

        public ValueTask<(IEnumerable<IReadOnlyDictionary<string, string>>, IEnumerable<IReadOnlyDictionary<string, string>>)>
        OpStringStringDSAsync(
            Dictionary<string, string>[] p1,
            Dictionary<string, string>[] p2,
            Current current,
            CancellationToken cancel)
        {
            var p3 = new Dictionary<string, string>[p1.Length + p2.Length];
            Array.Copy(p2, p3, p2.Length);
            Array.Copy(p1, 0, p3, p2.Length, p1.Length);

            var r = new Dictionary<string, string>[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                r[i] = p1[^(i + 1)];
            }
            return new((r, p3));
        }

        public ValueTask<(IEnumerable<IReadOnlyDictionary<string, MyEnum>>, IEnumerable<IReadOnlyDictionary<string, MyEnum>>)>
        OpStringMyEnumDSAsync(
            Dictionary<string, MyEnum>[] p1,
            Dictionary<string, MyEnum>[] p2,
            Current current,
            CancellationToken cancel)
        {
            var p3 = new Dictionary<string, MyEnum>[p1.Length + p2.Length];
            Array.Copy(p2, p3, p2.Length);
            Array.Copy(p1, 0, p3, p2.Length, p1.Length);

            var r = new Dictionary<string, MyEnum>[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                r[i] = p1[^(i + 1)];
            }
            return new((r, p3));
        }

        public ValueTask<(IEnumerable<IReadOnlyDictionary<MyEnum, string>>, IEnumerable<IReadOnlyDictionary<MyEnum, string>>)>
        OpMyEnumStringDSAsync(
            Dictionary<MyEnum, string>[] p1,
            Dictionary<MyEnum, string>[] p2,
            Current current,
            CancellationToken cancel)
        {
            var p3 = new Dictionary<MyEnum, string>[p1.Length + p2.Length];
            Array.Copy(p2, p3, p2.Length);
            Array.Copy(p1, 0, p3, p2.Length, p1.Length);

            var r = new Dictionary<MyEnum, string>[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                r[i] = p1[^(i + 1)];
            }
            return new((r, p3));
        }

        public ValueTask<(IEnumerable<IReadOnlyDictionary<MyStruct, MyEnum>>, IEnumerable<IReadOnlyDictionary<MyStruct, MyEnum>>)>
        OpMyStructMyEnumDSAsync(
            Dictionary<MyStruct, MyEnum>[] p1,
            Dictionary<MyStruct, MyEnum>[] p2,
            Current current,
            CancellationToken cancel)
        {
            var p3 = new Dictionary<MyStruct, MyEnum>[p1.Length + p2.Length];
            Array.Copy(p2, p3, p2.Length);
            Array.Copy(p1, 0, p3, p2.Length, p1.Length);

            var r = new Dictionary<MyStruct, MyEnum>[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                r[i] = p1[^(i + 1)];
            }
            return new((r, p3));
        }

        public ValueTask<(IReadOnlyDictionary<byte, byte[]>, IReadOnlyDictionary<byte, byte[]>)>
        OpByteByteSDAsync(
            Dictionary<byte, byte[]> p1,
            Dictionary<byte, byte[]> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p2));

        public ValueTask<(IReadOnlyDictionary<bool, bool[]>, IReadOnlyDictionary<bool, bool[]>)>
        OpBoolBoolSDAsync(
            Dictionary<bool, bool[]> p1,
            Dictionary<bool, bool[]> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p2));

        public ValueTask<(IReadOnlyDictionary<short, short[]>, IReadOnlyDictionary<short, short[]>)>
        OpShortShortSDAsync(
            Dictionary<short, short[]> p1,
            Dictionary<short, short[]> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p2));

        public ValueTask<(IReadOnlyDictionary<ushort, ushort[]>, IReadOnlyDictionary<ushort, ushort[]>)>
        OpUShortUShortSDAsync(
            Dictionary<ushort, ushort[]> p1,
            Dictionary<ushort, ushort[]> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p2));

        public ValueTask<(IReadOnlyDictionary<int, int[]>, IReadOnlyDictionary<int, int[]>)>
        OpIntIntSDAsync(
            Dictionary<int, int[]> p1,
            Dictionary<int, int[]> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p2));

        public ValueTask<(IReadOnlyDictionary<uint, uint[]>, IReadOnlyDictionary<uint, uint[]>)>
        OpUIntUIntSDAsync(
            Dictionary<uint, uint[]> p1,
            Dictionary<uint, uint[]> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p2));

        public ValueTask<(IReadOnlyDictionary<long, long[]>, IReadOnlyDictionary<long, long[]>)>
        OpLongLongSDAsync(
            Dictionary<long, long[]> p1,
            Dictionary<long, long[]> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p2));

        public ValueTask<(IReadOnlyDictionary<ulong, ulong[]>, IReadOnlyDictionary<ulong, ulong[]>)>
        OpULongULongSDAsync(
            Dictionary<ulong, ulong[]> p1,
            Dictionary<ulong, ulong[]> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p2));

        public ValueTask<(IReadOnlyDictionary<string, float[]>, IReadOnlyDictionary<string, float[]>)>
        OpStringFloatSDAsync(
            Dictionary<string, float[]> p1,
            Dictionary<string, float[]> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p2));

        public ValueTask<(IReadOnlyDictionary<string, double[]>, IReadOnlyDictionary<string, double[]>)>
        OpStringDoubleSDAsync(
            Dictionary<string, double[]> p1,
            Dictionary<string, double[]> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p2));

        public ValueTask<(IReadOnlyDictionary<string, string[]>, IReadOnlyDictionary<string, string[]>)>
        OpStringStringSDAsync(
            Dictionary<string, string[]> p1,
            Dictionary<string, string[]> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p2));

        public ValueTask<(IReadOnlyDictionary<MyEnum, MyEnum[]>, IReadOnlyDictionary<MyEnum, MyEnum[]>)>
        OpMyEnumMyEnumSDAsync(
            Dictionary<MyEnum, MyEnum[]> p1,
            Dictionary<MyEnum, MyEnum[]> p2,
            Current current,
            CancellationToken cancel) => new((MergeDictionaries(p1, p2), p2));

        public ValueTask<ReadOnlyMemory<int>> OpIntSAsync(int[] s, Current current, CancellationToken cancel)
        {
            int[] r = new int[s.Length];
            for (int i = 0; i < s.Length; ++i)
            {
                r[i] = -s[i];
            }

            return new(r);
        }

        public ValueTask<IReadOnlyDictionary<string, string>> OpContextAsync(
            Current current,
            CancellationToken cancel) =>
            new(current.Context);

        public ValueTask OpByteSOnewayAsync(byte[] s, Current current, CancellationToken cancel)
        {
            lock (_mutex)
            {
                ++_opByteSOnewayCallCount;
            }
            return default;
        }

        public ValueTask<int> OpByteSOnewayCallCountAsync(Current current, CancellationToken cancel)
        {
            lock (_mutex)
            {
                int count = _opByteSOnewayCallCount;
                _opByteSOnewayCallCount = 0;
                return new(count);
            }
        }

        public ValueTask OpDoubleMarshalingAsync(double p1, double[] p2, Current current, CancellationToken cancel)
        {
            double d = 1278312346.0 / 13.0;
            TestHelper.Assert(p1 == d);
            for (int i = 0; i < p2.Length; ++i)
            {
                TestHelper.Assert(p2[i] == d);
            }
            return default;
        }

        public ValueTask<(IEnumerable<string>, IEnumerable<string>)> OpStringSAsync(
            string[] p1,
            string[] p2,
            Current current,
            CancellationToken cancel)
        {
            string[] p3 = new string[p1.Length + p2.Length];
            Array.Copy(p1, p3, p1.Length);
            Array.Copy(p2, 0, p3, p1.Length, p2.Length);

            string[] r = new string[p1.Length];
            for (int i = 0; i < p1.Length; i++)
            {
                r[i] = p1[^(i + 1)];
            }
            return new((r, p3));
        }

        public ValueTask<(IEnumerable<IEnumerable<string>>, IEnumerable<IEnumerable<string>>)> OpStringSSAsync(
            string[][] p1,
            string[][] p2,
            Current current,
            CancellationToken cancel)
        {
            string[][] p3 = new string[p1.Length + p2.Length][];
            Array.Copy(p1, p3, p1.Length);
            Array.Copy(p2, 0, p3, p1.Length, p2.Length);
            string[][] r = new string[p2.Length][];
            for (int i = 0; i < p2.Length; i++)
            {
                r[i] = p2[^(i + 1)];
            }
            return new((r, p3));
        }

        public ValueTask<(IEnumerable<IEnumerable<IEnumerable<string>>>, IEnumerable<IEnumerable<IEnumerable<string>>>)>
        OpStringSSSAsync(string[][][] p1, string[][][] p2, Current current, CancellationToken cancel)
        {
            string[][][] p3 = new string[p1.Length + p2.Length][][];
            Array.Copy(p1, p3, p1.Length);
            Array.Copy(p2, 0, p3, p1.Length, p2.Length);

            string[][][] r = new string[p2.Length][][];
            for (int i = 0; i < p2.Length; i++)
            {
                r[i] = p2[^(i + 1)];
            }
            return new((r, p3));
        }

        public ValueTask<(IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>)>
        OpStringStringDAsync(
            Dictionary<string, string> p1,
            Dictionary<string, string> p2,
            Current current,
            CancellationToken cancel) =>
            new((MergeDictionaries(p1, p2), p1));

        public ValueTask<(Structure, Structure)> OpStructAsync(
            Structure p1,
            Structure p2,
            Current current,
            CancellationToken cancel)
        {
            Structure p3 = p1;
            p3.S.S = "a new string";
            return new((p2, p3));
        }

        public ValueTask OpIdempotentAsync(Current current, CancellationToken cancel)
        {
            TestHelper.Assert(current.IsIdempotent);
            return default;
        }

        // "return" exception when called two-way, otherwise succeeds.
        public ValueTask OpOnewayAsync(Current current, CancellationToken cancel) => throw new SomeException();

        // "return" exception when called two-way, otherwise succeeds.
        public ValueTask OpOnewayMetadataAsync(Current current, CancellationToken cancel) => throw new SomeException();

        public ValueTask OpDerivedAsync(Current current, CancellationToken cancel) => default;

        public ValueTask<byte> OpByte1Async(byte value, Current current, CancellationToken cancel) =>
            new(value);

        public ValueTask<short> OpShort1Async(short value, Current current, CancellationToken cancel) =>
            new(value);
        public ValueTask<int> OpInt1Async(int value, Current current, CancellationToken cancel) =>
            new(value);
        public ValueTask<long> OpLong1Async(long value, Current current, CancellationToken cancel) =>
            new(value);

        public ValueTask<ushort> OpUShort1Async(ushort value, Current current, CancellationToken cancel) =>
            new(value);
        public ValueTask<uint> OpUInt1Async(uint value, Current current, CancellationToken cancel) =>
            new(value);
        public ValueTask<ulong> OpULong1Async(ulong value, Current current, CancellationToken cancel) =>
            new(value);

        public ValueTask<float> OpFloat1Async(float value, Current current, CancellationToken cancel) =>
            new(value);

        public ValueTask<double> OpDouble1Async(double value, Current current, CancellationToken cancel) =>
            new(value);

        public ValueTask<string> OpString1Async(string value, Current current, CancellationToken cancel) =>
            new(value);

        public ValueTask<IEnumerable<string>> OpStringS1Async(
            string[] value,
            Current current,
            CancellationToken cancel) =>
            new(value);

        public ValueTask<IReadOnlyDictionary<byte, bool>> OpByteBoolD1Async(
            Dictionary<byte, bool> value,
            Current current,
            CancellationToken cancel) => new(value);

        public ValueTask<IEnumerable<string>> OpStringS2Async(
            string[] value,
            Current current,
            CancellationToken cancel) =>
            new(value);

        public ValueTask<IReadOnlyDictionary<byte, bool>> OpByteBoolD2Async(
            Dictionary<byte, bool> value,
            Current current,
            CancellationToken cancel) => new(value);

        public ValueTask<MyClass1?> OpMyClass1Async(MyClass1? value, Current current, CancellationToken cancel) =>
            new(value);

        public ValueTask<MyStruct1> OpMyStruct1Async(MyStruct1 value, Current current, CancellationToken cancel) =>
            new(value);

        public async ValueTask<IMyClass.OpMStruct1MarshaledReturnValue> OpMStruct1Async(Current current, CancellationToken cancel)
        {
            await Task.Delay(0, cancel);
            return new IMyClass.OpMStruct1MarshaledReturnValue(
                new Structure(null, MyEnum.enum1, new AnotherStruct("")), current);
        }

        public async ValueTask<IMyClass.OpMStruct2MarshaledReturnValue> OpMStruct2Async(
            Structure p1,
            Current current,
            CancellationToken cancel)
        {
            await Task.Delay(0, cancel);
            return new IMyClass.OpMStruct2MarshaledReturnValue(p1, p1, current);
        }

        public async ValueTask<IMyClass.OpMSeq1MarshaledReturnValue> OpMSeq1Async(
            Current current,
            CancellationToken cancel)
        {
            await Task.Delay(0, cancel);
            return new IMyClass.OpMSeq1MarshaledReturnValue(Array.Empty<string>(), current);
        }

        public async ValueTask<IMyClass.OpMSeq2MarshaledReturnValue> OpMSeq2Async(
            string[] p1,
            Current current,
            CancellationToken cancel)
        {
            await Task.Delay(0, cancel);
            return new IMyClass.OpMSeq2MarshaledReturnValue(p1, p1, current);
        }

        public async ValueTask<IMyClass.OpMDict1MarshaledReturnValue> OpMDict1Async(
            Current current,
            CancellationToken cancel)
        {
            await Task.Delay(0, cancel);
            return new IMyClass.OpMDict1MarshaledReturnValue(new Dictionary<string, string>(), current);
        }

        public async ValueTask<IMyClass.OpMDict2MarshaledReturnValue> OpMDict2Async(
            Dictionary<string, string> p1,
            Current current,
            CancellationToken cancel)
        {
            await Task.Delay(0, cancel);
            return new IMyClass.OpMDict2MarshaledReturnValue(p1, p1, current);
        }

        public ValueTask OpSendStream1Async(Stream p1, Current current, CancellationToken cancel)
        {
            ConsumeStream(-1, p1);
            return default;
        }

        public ValueTask OpSendStream2Async(int p1, Stream p2, Current current, CancellationToken cancel)
        {
            ConsumeStream(p1, p2);
            return default;
        }

        public ValueTask<Stream> OpGetStream1Async(Current current, CancellationToken cancel)
        {
            byte[] buffer = new byte[1024];
            for (int i = 0; i < buffer.Length; ++i)
            {
                buffer[i] = (byte)(i % 256);
            }
            _streams.Add(new MemoryStream(buffer));
            return new(_streams.Last());
        }

        public ValueTask<(int R1, Stream R2)> OpGetStream2Async(Current current, CancellationToken cancel)
        {
            byte[] buffer = new byte[1024];
            for (int i = 0; i < buffer.Length; ++i)
            {
                buffer[i] = (byte)(i % 256);
            }
            _streams.Add(new MemoryStream(buffer));
            return new((1024, _streams.Last()));
        }

        public ValueTask<Stream> OpSendAndGetStream1Async(Stream p1, Current current, CancellationToken cancel) =>
            new(p1);

        public ValueTask<(int R1, Stream R2)> OpSendAndGetStream2Async(
            int p1,
            Stream p2,
            Current current,
            CancellationToken cancel) => new((p1, p2));

        private static void ConsumeStream(int receivedSize, Stream stream)
        {
            var buffer = new byte[1024];
            int size = 0;
            while (true)
            {
                int read = stream.Read(new Span<byte>(buffer));
                if (read == 0)
                {
                    break;
                }
                size += read;
            }
            TestHelper.Assert(receivedSize == -1 || receivedSize == size);
        }

        private static IReadOnlyDictionary<TKey, TValue> MergeDictionaries<TKey, TValue>(
            IReadOnlyDictionary<TKey, TValue> first,
            IReadOnlyDictionary<TKey, TValue> second) where TKey : notnull
        {
            var result = new Dictionary<TKey, TValue>(first);
            foreach ((TKey key, TValue value) in second)
            {
                result[key] = value;
            }
            return result;
        }
    }
}
