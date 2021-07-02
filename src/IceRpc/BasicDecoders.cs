// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Provides decoders for all the basic types.</summary>
    public static class BasicDecoders
    {
        /// <summary>A <see cref="Decoder{T}"/> for <c>bool</c> values.</summary>
        public static readonly Decoder<bool> BoolDecoder =
            reader => reader.ReadBool();

        /// <summary>A <see cref="Decoder{T}"/> for <c>byte</c> values.</summary>
        public static readonly Decoder<byte> ByteDecoder =
            reader => reader.ReadByte();

        /// <summary>A <see cref="Decoder{T}"/> for <c>double</c> values.</summary>
        public static readonly Decoder<double> DoubleDecoder =
            reader => reader.ReadDouble();

        /// <summary>A <see cref="Decoder{T}"/> for <c>float</c> values.</summary>
        public static readonly Decoder<float> FloatDecoder =
            reader => reader.ReadFloat();

        /// <summary>A <see cref="Decoder{T}"/> for <c>int</c> values.</summary>
        public static readonly Decoder<int> IntDecoder =
            reader => reader.ReadInt();

        /// <summary>A <see cref="Decoder{T}"/> for <c>long</c> values.</summary>
        public static readonly Decoder<long> LongDecoder =
            reader => reader.ReadLong();

        /// <summary>A <see cref="Decoder{T}"/> for <c>short</c> values.</summary>
        public static readonly Decoder<short> ShortDecoder =
            reader => reader.ReadShort();

        /// <summary>A <see cref="Decoder{T}"/> for <c>string</c> instances.</summary>
        public static readonly Decoder<string> StringDecoder =
            reader => reader.ReadString();

        /// <summary>A <see cref="Decoder{T}"/> for <c>uint</c> values.</summary>
        public static readonly Decoder<uint> UIntDecoder =
            reader => reader.ReadUInt();

        /// <summary>A <see cref="Decoder{T}"/> for <c>ulong</c> values.</summary>
        public static readonly Decoder<ulong> ULongDecoder =
            reader => reader.ReadULong();

        /// <summary>A <see cref="Decoder{T}"/> for <c>ushort</c> values.</summary>
        public static readonly Decoder<ushort> UShortDecoder =
            reader => reader.ReadUShort();

        /// <summary>A <see cref="Decoder{T}"/> for var int values.</summary>
        public static readonly Decoder<int> VarIntDecoder =
            reader => reader.ReadVarInt();

        /// <summary>A <see cref="Decoder{T}"/> for var long values.</summary>
        public static readonly Decoder<long> VarLongDecoder =
            reader => reader.ReadVarLong();

        /// <summary>A <see cref="Decoder{T}"/> for var uint values.</summary>
        public static readonly Decoder<uint> VarUIntDecoder =
            reader => reader.ReadVarUInt();

        /// <summary>A <see cref="Decoder{T}"/> for var ulong values.</summary>
        public static readonly Decoder<ulong> VarULongDecoder =
            reader => reader.ReadVarULong();
    }
}
