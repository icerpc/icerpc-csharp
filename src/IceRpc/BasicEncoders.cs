// Copyright (c) ZeroC, Inc. All rights reserved.

using System.ComponentModel;

namespace IceRpc
{
    /// <summary>Writes data into a byte buffer using the Ice encoding.</summary>
    public sealed partial class BufferWriter
    {
        // Cached Encoder static objects used by the generated code

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>bool</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<bool> BoolEncoder =
            (writer, value) => writer.WriteBool(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>byte</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<byte> ByteEncoder =
            (writer, value) => writer.WriteByte(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>double</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<double> DoubleEncoder =
            (writer, value) => writer.WriteDouble(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>float</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<float> FloatEncoder =
            (writer, value) => writer.WriteFloat(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>int</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<int> IntEncoder =
            (writer, value) => writer.WriteInt(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>long</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<long> LongEncoder =
            (writer, value) => writer.WriteLong(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>short</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<short> ShortEncoder =
            (writer, value) => writer.WriteShort(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>string</c> instances.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<string> StringEncoder =
            (writer, value) => writer.WriteString(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>uint</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<uint> UIntEncoder =
            (writer, value) => writer.WriteUInt(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>ulong</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<ulong> ULongEncoder =
            (writer, value) => writer.WriteULong(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>ushort</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<ushort> UShortEncoder =
            (writer, value) => writer.WriteUShort(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write var int values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<int> VarIntEncoder =
            (writer, value) => writer.WriteVarInt(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write var long values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<long> VarLongEncoder =
            (writer, value) => writer.WriteVarLong(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write var uint values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<uint> VarUIntEncoder =
            (writer, value) => writer.WriteVarUInt(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write var ulong values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<ulong> VarULongEncoder =
            (writer, value) => writer.WriteVarULong(value);
    }
}
