// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Provides decode functions for all the basic types.</summary>
    public static class BasicDecodeFuncs
    {
        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>bool</c> values.</summary>
        public static readonly DecodeFunc<bool> BoolDecodeFunc =
            decoder => decoder.DecodeBool();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>byte</c> values.</summary>
        public static readonly DecodeFunc<byte> ByteDecodeFunc =
            decoder => decoder.DecodeByte();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>double</c> values.</summary>
        public static readonly DecodeFunc<double> DoubleDecodeFunc =
            decoder => decoder.DecodeDouble();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>float</c> values.</summary>
        public static readonly DecodeFunc<float> FloatDecodeFunc =
            decoder => decoder.DecodeFloat();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>int</c> values.</summary>
        public static readonly DecodeFunc<int> IntDecodeFunc =
            decoder => decoder.DecodeInt();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>long</c> values.</summary>
        public static readonly DecodeFunc<long> LongDecodeFunc =
            decoder => decoder.DecodeLong();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>short</c> values.</summary>
        public static readonly DecodeFunc<short> ShortDecodeFunc =
            decoder => decoder.DecodeShort();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>string</c> instances.</summary>
        public static readonly DecodeFunc<string> StringDecodeFunc =
            decoder => decoder.DecodeString();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>uint</c> values.</summary>
        public static readonly DecodeFunc<uint> UIntDecodeFunc =
            decoder => decoder.DecodeUInt();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>ulong</c> values.</summary>
        public static readonly DecodeFunc<ulong> ULongDecodeFunc =
            decoder => decoder.DecodeULong();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>ushort</c> values.</summary>
        public static readonly DecodeFunc<ushort> UShortDecodeFunc =
            decoder => decoder.DecodeUShort();

        /// <summary>A <see cref="DecodeFunc{T}"/> for var int values.</summary>
        public static readonly DecodeFunc<int> VarIntDecodeFunc =
            decoder => decoder.DecodeVarInt();

        /// <summary>A <see cref="DecodeFunc{T}"/> for var long values.</summary>
        public static readonly DecodeFunc<long> VarLongDecodeFunc =
            decoder => decoder.DecodeVarLong();

        /// <summary>A <see cref="DecodeFunc{T}"/> for var uint values.</summary>
        public static readonly DecodeFunc<uint> VarUIntDecodeFunc =
            decoder => decoder.DecodeVarUInt();

        /// <summary>A <see cref="DecodeFunc{T}"/> for var ulong values.</summary>
        public static readonly DecodeFunc<ulong> VarULongDecodeFunc =
            decoder => decoder.DecodeVarULong();
    }
}
