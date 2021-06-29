// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Provides encoders for all the basic types.</summary>
    public static class BasicEncoders
    {
        // Cached Encoder static objects used by the generated code

        /// <summary>An <see cref="Encoder{T}"/> for <c>bool</c> values.</summary>
        public static readonly Encoder<bool> BoolEncoder =
            (writer, value) => writer.WriteBool(value);

        /// <summary>An <see cref="Encoder{T}"/> for <c>byte</c> values.</summary>
        public static readonly Encoder<byte> ByteEncoder =
            (writer, value) => writer.WriteByte(value);

        /// <summary>An <see cref="Encoder{T}"/> for <c>double</c> values.</summary>
        public static readonly Encoder<double> DoubleEncoder =
            (writer, value) => writer.WriteDouble(value);

        /// <summary>An <see cref="Encoder{T}"/> for <c>float</c> values.</summary>
        public static readonly Encoder<float> FloatEncoder =
            (writer, value) => writer.WriteFloat(value);

        /// <summary>An <see cref="Encoder{T}"/> for <c>int</c> values.</summary>
        public static readonly Encoder<int> IntEncoder =
            (writer, value) => writer.WriteInt(value);

        /// <summary>An <see cref="Encoder{T}"/> for <c>long</c> values.</summary>
        public static readonly Encoder<long> LongEncoder =
            (writer, value) => writer.WriteLong(value);

        /// <summary>An <see cref="Encoder{T}"/> for <c>short</c> values.</summary>
        public static readonly Encoder<short> ShortEncoder =
            (writer, value) => writer.WriteShort(value);

        /// <summary>An <see cref="Encoder{T}"/> for <c>string</c> instances.</summary>
        public static readonly Encoder<string> StringEncoder =
            (writer, value) => writer.WriteString(value);

        /// <summary>An <see cref="Encoder{T}"/> for <c>uint</c> values.</summary>
        public static readonly Encoder<uint> UIntEncoder =
            (writer, value) => writer.WriteUInt(value);

        /// <summary>An <see cref="Encoder{T}"/> for <c>ulong</c> values.</summary>
        public static readonly Encoder<ulong> ULongEncoder =
            (writer, value) => writer.WriteULong(value);

        /// <summary>An <see cref="Encoder{T}"/> for <c>ushort</c> values.</summary>
        public static readonly Encoder<ushort> UShortEncoder =
            (writer, value) => writer.WriteUShort(value);

        /// <summary>An <see cref="Encoder{T}"/> for var int values.</summary>
        public static readonly Encoder<int> VarIntEncoder =
            (writer, value) => writer.WriteVarInt(value);

        /// <summary>An <see cref="Encoder{T}"/> for var long values.</summary>
        public static readonly Encoder<long> VarLongEncoder =
            (writer, value) => writer.WriteVarLong(value);

        /// <summary>An <see cref="Encoder{T}"/> for var uint values.</summary>
        public static readonly Encoder<uint> VarUIntEncoder =
            (writer, value) => writer.WriteVarUInt(value);

        /// <summary>An <see cref="Encoder{T}"/> for var ulong values.</summary>
        public static readonly Encoder<ulong> VarULongEncoder =
            (writer, value) => writer.WriteVarULong(value);
    }
}
