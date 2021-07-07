// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Provides Ice writers for all the basic types.</summary>
    public static class BasicIceWriters
    {
        /// <summary>An <see cref="IceWriter{T}"/> for <c>bool</c> values.</summary>
        public static readonly IceWriter<bool> BoolIceWriter =
            (iceEncoder, value) => iceEncoder.WriteBool(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>byte</c> values.</summary>
        public static readonly IceWriter<byte> ByteIceWriter =
            (iceEncoder, value) => iceEncoder.WriteByte(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>double</c> values.</summary>
        public static readonly IceWriter<double> DoubleIceWriter =
            (iceEncoder, value) => iceEncoder.WriteDouble(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>float</c> values.</summary>
        public static readonly IceWriter<float> FloatIceWriter =
            (iceEncoder, value) => iceEncoder.WriteFloat(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>int</c> values.</summary>
        public static readonly IceWriter<int> IntIceWriter =
            (iceEncoder, value) => iceEncoder.WriteInt(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>long</c> values.</summary>
        public static readonly IceWriter<long> LongIceWriter =
            (iceEncoder, value) => iceEncoder.WriteLong(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>short</c> values.</summary>
        public static readonly IceWriter<short> ShortIceWriter =
            (iceEncoder, value) => iceEncoder.WriteShort(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>string</c> instances.</summary>
        public static readonly IceWriter<string> StringIceWriter =
            (iceEncoder, value) => iceEncoder.WriteString(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>uint</c> values.</summary>
        public static readonly IceWriter<uint> UIntIceWriter =
            (iceEncoder, value) => iceEncoder.WriteUInt(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>ulong</c> values.</summary>
        public static readonly IceWriter<ulong> ULongIceWriter =
            (iceEncoder, value) => iceEncoder.WriteULong(value);

        /// <summary>An <see cref="IceWriter{T}"/> for <c>ushort</c> values.</summary>
        public static readonly IceWriter<ushort> UShortIceWriter =
            (iceEncoder, value) => iceEncoder.WriteUShort(value);

        /// <summary>An <see cref="IceWriter{T}"/> for var int values.</summary>
        public static readonly IceWriter<int> VarIntIceWriter =
            (iceEncoder, value) => iceEncoder.WriteVarInt(value);

        /// <summary>An <see cref="IceWriter{T}"/> for var long values.</summary>
        public static readonly IceWriter<long> VarLongIceWriter =
            (iceEncoder, value) => iceEncoder.WriteVarLong(value);

        /// <summary>An <see cref="IceWriter{T}"/> for var uint values.</summary>
        public static readonly IceWriter<uint> VarUIntIceWriter =
            (iceEncoder, value) => iceEncoder.WriteVarUInt(value);

        /// <summary>An <see cref="IceWriter{T}"/> for var ulong values.</summary>
        public static readonly IceWriter<ulong> VarULongIceWriter =
            (iceEncoder, value) => iceEncoder.WriteVarULong(value);
    }
}
