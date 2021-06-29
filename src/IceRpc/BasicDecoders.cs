// Copyright (c) ZeroC, Inc. All rights reserved.

using System.ComponentModel;

namespace IceRpc
{
    /// <summary>Reads a byte buffer encoded using the Ice encoding.</summary>
    public static class BasicDecoders
    {
        // Cached Decoder static objects used by the generated code

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>bool</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<bool> BoolDecoder =
            reader => reader.ReadBool();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>byte</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<byte> ByteDecoder =
            reader => reader.ReadByte();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>double</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<double> DoubleDecoder =
            reader => reader.ReadDouble();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>float</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<float> FloatDecoder =
            reader => reader.ReadFloat();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>int</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<int> IntDecoder =
            reader => reader.ReadInt();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>long</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<long> LongDecoder =
            reader => reader.ReadLong();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>short</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<short> ShortDecoder =
            reader => reader.ReadShort();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>string</c> instances.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<string> StringDecoder =
            reader => reader.ReadString();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>uint</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<uint> UIntDecoder =
            reader => reader.ReadUInt();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>ulong</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<ulong> ULongDecoder =
            reader => reader.ReadULong();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>ushort</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<ushort> UShortDecoder =
            reader => reader.ReadUShort();

        /// <summary>A <see cref="Decoder{T}"/> used to read var int values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<int> VarIntDecoder =
            reader => reader.ReadVarInt();

        /// <summary>A <see cref="Decoder{T}"/> used to read var long values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<long> VarLongDecoder =
            reader => reader.ReadVarLong();

        /// <summary>A <see cref="Decoder{T}"/> used to read var uint values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<uint> VarUIntDecoder =
            reader => reader.ReadVarUInt();

        /// <summary>A <see cref="Decoder{T}"/> used to read var ulong values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<ulong> VarULongDecoder =
            reader => reader.ReadVarULong();
    }
}
