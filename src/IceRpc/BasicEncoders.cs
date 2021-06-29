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
        public static readonly Encoder<bool> IceWriterFromBool =
            (writer, value) => writer.WriteBool(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>byte</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<byte> IceWriterFromByte =
            (writer, value) => writer.WriteByte(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>double</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<double> IceWriterFromDouble =
            (writer, value) => writer.WriteDouble(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>float</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<float> IceWriterFromFloat =
            (writer, value) => writer.WriteFloat(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>int</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<int> IceWriterFromInt =
            (writer, value) => writer.WriteInt(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>long</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<long> IceWriterFromLong =
            (writer, value) => writer.WriteLong(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>short</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<short> IceWriterFromShort =
            (writer, value) => writer.WriteShort(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>string</c> instances.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<string> IceWriterFromString =
            (writer, value) => writer.WriteString(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>uint</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<uint> IceWriterFromUInt =
            (writer, value) => writer.WriteUInt(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>ulong</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<ulong> IceWriterFromULong =
            (writer, value) => writer.WriteULong(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write <c>ushort</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<ushort> IceWriterFromUShort =
            (writer, value) => writer.WriteUShort(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write var int values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<int> IceWriterFromVarInt =
            (writer, value) => writer.WriteVarInt(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write var long values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<long> IceWriterFromVarLong =
            (writer, value) => writer.WriteVarLong(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write var uint values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<uint> IceWriterFromVarUInt =
            (writer, value) => writer.WriteVarUInt(value);

        /// <summary>A <see cref="Encoder{T}"/> used to write var ulong values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Encoder<ulong> IceWriterFromVarULong =
            (writer, value) => writer.WriteVarULong(value);
    }
}
