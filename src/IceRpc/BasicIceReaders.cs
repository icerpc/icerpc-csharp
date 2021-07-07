// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Provides Ice readers for all the basic types.</summary>
    public static class BasicIceReaders
    {
        /// <summary>A <see cref="IceReader{T}"/> for <c>bool</c> values.</summary>
        public static readonly IceReader<bool> BoolIceReader =
            decoder => decoder.ReadBool();

        /// <summary>A <see cref="IceReader{T}"/> for <c>byte</c> values.</summary>
        public static readonly IceReader<byte> ByteIceReader =
            decoder => decoder.ReadByte();

        /// <summary>A <see cref="IceReader{T}"/> for <c>double</c> values.</summary>
        public static readonly IceReader<double> DoubleIceReader =
            decoder => decoder.ReadDouble();

        /// <summary>A <see cref="IceReader{T}"/> for <c>float</c> values.</summary>
        public static readonly IceReader<float> FloatIceReader =
            decoder => decoder.ReadFloat();

        /// <summary>A <see cref="IceReader{T}"/> for <c>int</c> values.</summary>
        public static readonly IceReader<int> IntIceReader =
            decoder => decoder.ReadInt();

        /// <summary>A <see cref="IceReader{T}"/> for <c>long</c> values.</summary>
        public static readonly IceReader<long> LongIceReader =
            decoder => decoder.ReadLong();

        /// <summary>A <see cref="IceReader{T}"/> for <c>short</c> values.</summary>
        public static readonly IceReader<short> ShortIceReader =
            decoder => decoder.ReadShort();

        /// <summary>A <see cref="IceReader{T}"/> for <c>string</c> instances.</summary>
        public static readonly IceReader<string> StringIceReader =
            decoder => decoder.ReadString();

        /// <summary>A <see cref="IceReader{T}"/> for <c>uint</c> values.</summary>
        public static readonly IceReader<uint> UIntIceReader =
            decoder => decoder.ReadUInt();

        /// <summary>A <see cref="IceReader{T}"/> for <c>ulong</c> values.</summary>
        public static readonly IceReader<ulong> ULongIceReader =
            decoder => decoder.ReadULong();

        /// <summary>A <see cref="IceReader{T}"/> for <c>ushort</c> values.</summary>
        public static readonly IceReader<ushort> UShortIceReader =
            decoder => decoder.ReadUShort();

        /// <summary>A <see cref="IceReader{T}"/> for var int values.</summary>
        public static readonly IceReader<int> VarIntIceReader =
            decoder => decoder.ReadVarInt();

        /// <summary>A <see cref="IceReader{T}"/> for var long values.</summary>
        public static readonly IceReader<long> VarLongIceReader =
            decoder => decoder.ReadVarLong();

        /// <summary>A <see cref="IceReader{T}"/> for var uint values.</summary>
        public static readonly IceReader<uint> VarUIntIceReader =
            decoder => decoder.ReadVarUInt();

        /// <summary>A <see cref="IceReader{T}"/> for var ulong values.</summary>
        public static readonly IceReader<ulong> VarULongIceReader =
            decoder => decoder.ReadVarULong();
    }
}
