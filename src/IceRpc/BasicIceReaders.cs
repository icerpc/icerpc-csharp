// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Provides Ice readers for all the basic types.</summary>
    public static class BasicIceReaders
    {
        /// <summary>A <see cref="IceReader{T}"/> for <c>bool</c> values.</summary>
        public static readonly IceReader<bool> BoolIceReader =
            reader => reader.ReadBool();

        /// <summary>A <see cref="IceReader{T}"/> for <c>byte</c> values.</summary>
        public static readonly IceReader<byte> ByteIceReader =
            reader => reader.ReadByte();

        /// <summary>A <see cref="IceReader{T}"/> for <c>double</c> values.</summary>
        public static readonly IceReader<double> DoubleIceReader =
            reader => reader.ReadDouble();

        /// <summary>A <see cref="IceReader{T}"/> for <c>float</c> values.</summary>
        public static readonly IceReader<float> FloatIceReader =
            reader => reader.ReadFloat();

        /// <summary>A <see cref="IceReader{T}"/> for <c>int</c> values.</summary>
        public static readonly IceReader<int> IntIceReader =
            reader => reader.ReadInt();

        /// <summary>A <see cref="IceReader{T}"/> for <c>long</c> values.</summary>
        public static readonly IceReader<long> LongIceReader =
            reader => reader.ReadLong();

        /// <summary>A <see cref="IceReader{T}"/> for <c>short</c> values.</summary>
        public static readonly IceReader<short> ShortIceReader =
            reader => reader.ReadShort();

        /// <summary>A <see cref="IceReader{T}"/> for <c>string</c> instances.</summary>
        public static readonly IceReader<string> StringIceReader =
            reader => reader.ReadString();

        /// <summary>A <see cref="IceReader{T}"/> for <c>uint</c> values.</summary>
        public static readonly IceReader<uint> UIntIceReader =
            reader => reader.ReadUInt();

        /// <summary>A <see cref="IceReader{T}"/> for <c>ulong</c> values.</summary>
        public static readonly IceReader<ulong> ULongIceReader =
            reader => reader.ReadULong();

        /// <summary>A <see cref="IceReader{T}"/> for <c>ushort</c> values.</summary>
        public static readonly IceReader<ushort> UShortIceReader =
            reader => reader.ReadUShort();

        /// <summary>A <see cref="IceReader{T}"/> for var int values.</summary>
        public static readonly IceReader<int> VarIntIceReader =
            reader => reader.ReadVarInt();

        /// <summary>A <see cref="IceReader{T}"/> for var long values.</summary>
        public static readonly IceReader<long> VarLongIceReader =
            reader => reader.ReadVarLong();

        /// <summary>A <see cref="IceReader{T}"/> for var uint values.</summary>
        public static readonly IceReader<uint> VarUIntIceReader =
            reader => reader.ReadVarUInt();

        /// <summary>A <see cref="IceReader{T}"/> for var ulong values.</summary>
        public static readonly IceReader<ulong> VarULongIceReader =
            reader => reader.ReadVarULong();
    }
}
