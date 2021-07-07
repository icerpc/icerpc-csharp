// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Provides Ice writers for all the basic types.</summary>
    public static class BasicIceWriters
    {
        /// <summary>An <see cref="IceWriter{T}"/> for <c>bool</c> values.</summary>
        public static readonly IceWriter<bool> BoolIceWriter =
            (encoder, value) => encoder.WriteBool(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>byte</c> values.</summary>
        public static readonly IceWriter<byte> ByteIceWriter =
            (encoder, value) => encoder.WriteByte(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>double</c> values.</summary>
        public static readonly IceWriter<double> DoubleIceWriter =
            (encoder, value) => encoder.WriteDouble(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>float</c> values.</summary>
        public static readonly IceWriter<float> FloatIceWriter =
            (encoder, value) => encoder.WriteFloat(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>int</c> values.</summary>
        public static readonly IceWriter<int> IntIceWriter =
            (encoder, value) => encoder.WriteInt(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>long</c> values.</summary>
        public static readonly IceWriter<long> LongIceWriter =
            (encoder, value) => encoder.WriteLong(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>short</c> values.</summary>
        public static readonly IceWriter<short> ShortIceWriter =
            (encoder, value) => encoder.WriteShort(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>string</c> instances.</summary>
        public static readonly IceWriter<string> StringIceWriter =
            (encoder, value) => encoder.WriteString(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>uint</c> values.</summary>
        public static readonly IceWriter<uint> UIntIceWriter =
            (encoder, value) => encoder.WriteUInt(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>ulong</c> values.</summary>
        public static readonly IceWriter<ulong> ULongIceWriter =
            (encoder, value) => encoder.WriteULong(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>ushort</c> values.</summary>
        public static readonly IceWriter<ushort> UShortIceWriter =
            (encoder, value) => encoder.WriteUShort(value);

        /// <summary>An <see cref="IceWriter{T}"/> for var int values.</summary>
        public static readonly IceWriter<int> VarIntIceWriter =
            (encoder, value) => encoder.WriteVarInt(value);

        /// <summary>An <see cref="IceWriter{T}"/> for var long values.</summary>
        public static readonly IceWriter<long> VarLongIceWriter =
            (encoder, value) => encoder.WriteVarLong(value);

        /// <summary>An <see cref="IceWriter{T}"/> for var uint values.</summary>
        public static readonly IceWriter<uint> VarUIntIceWriter =
            (encoder, value) => encoder.WriteVarUInt(value);

        /// <summary>An <see cref="IceWriter{T}"/> for var ulong values.</summary>
        public static readonly IceWriter<ulong> VarULongIceWriter =
            (encoder, value) => encoder.WriteVarULong(value);
    }
}
