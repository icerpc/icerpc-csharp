// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Provides Ice writers for all the basic types.</summary>
    public static class BasicIceWriters
    {
        /// <summary>An <see cref="IceWriter{T}"/> for <c>bool</c> values.</summary>
        public static readonly IceWriter<bool> BoolIceWriter =
            (writer, value) => writer.WriteBool(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>byte</c> values.</summary>
        public static readonly IceWriter<byte> ByteIceWriter =
            (writer, value) => writer.WriteByte(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>double</c> values.</summary>
        public static readonly IceWriter<double> DoubleIceWriter =
            (writer, value) => writer.WriteDouble(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>float</c> values.</summary>
        public static readonly IceWriter<float> FloatIceWriter =
            (writer, value) => writer.WriteFloat(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>int</c> values.</summary>
        public static readonly IceWriter<int> IntIceWriter =
            (writer, value) => writer.WriteInt(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>long</c> values.</summary>
        public static readonly IceWriter<long> LongIceWriter =
            (writer, value) => writer.WriteLong(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>short</c> values.</summary>
        public static readonly IceWriter<short> ShortIceWriter =
            (writer, value) => writer.WriteShort(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>string</c> instances.</summary>
        public static readonly IceWriter<string> StringIceWriter =
            (writer, value) => writer.WriteString(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>uint</c> values.</summary>
        public static readonly IceWriter<uint> UIntIceWriter =
            (writer, value) => writer.WriteUInt(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>ulong</c> values.</summary>
        public static readonly IceWriter<ulong> ULongIceWriter =
            (writer, value) => writer.WriteULong(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>ushort</c> values.</summary>
        public static readonly IceWriter<ushort> UShortIceWriter =
            (writer, value) => writer.WriteUShort(value);

        /// <summary>An <see cref="IceWriter{T}"/> for var int values.</summary>
        public static readonly IceWriter<int> VarIntIceWriter =
            (writer, value) => writer.WriteVarInt(value);

        /// <summary>An <see cref="IceWriter{T}"/> for var long values.</summary>
        public static readonly IceWriter<long> VarLongIceWriter =
            (writer, value) => writer.WriteVarLong(value);

        /// <summary>An <see cref="IceWriter{T}"/> for var uint values.</summary>
        public static readonly IceWriter<uint> VarUIntIceWriter =
            (writer, value) => writer.WriteVarUInt(value);

        /// <summary>An <see cref="IceWriter{T}"/> for var ulong values.</summary>
        public static readonly IceWriter<ulong> VarULongIceWriter =
            (writer, value) => writer.WriteVarULong(value);
    }
}
