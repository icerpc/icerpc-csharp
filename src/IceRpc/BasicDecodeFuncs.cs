// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Provides decode functions for all the basic types.</summary>
    public static class BasicDecodeFuncs
    {
        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>bool</c> values.</summary>
        public static readonly DecodeFunc<bool> BoolDecodeFunc =
            iceDecoder => iceDecoder.DecodeBool();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>byte</c> values.</summary>
        public static readonly DecodeFunc<byte> ByteDecodeFunc =
            iceDecoder => iceDecoder.DecodeByte();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>double</c> values.</summary>
        public static readonly DecodeFunc<double> DoubleDecodeFunc =
            iceDecoder => iceDecoder.DecodeDouble();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>float</c> values.</summary>
        public static readonly DecodeFunc<float> FloatDecodeFunc =
            iceDecoder => iceDecoder.DecodeFloat();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>int</c> values.</summary>
        public static readonly DecodeFunc<int> IntDecodeFunc =
            iceDecoder => iceDecoder.DecodeInt();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>long</c> values.</summary>
        public static readonly DecodeFunc<long> LongDecodeFunc =
            iceDecoder => iceDecoder.DecodeLong();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>short</c> values.</summary>
        public static readonly DecodeFunc<short> ShortDecodeFunc =
            iceDecoder => iceDecoder.DecodeShort();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>string</c> instances.</summary>
        public static readonly DecodeFunc<string> StringDecodeFunc =
            iceDecoder => iceDecoder.DecodeString();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>uint</c> values.</summary>
        public static readonly DecodeFunc<uint> UIntDecodeFunc =
            iceDecoder => iceDecoder.DecodeUInt();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>ulong</c> values.</summary>
        public static readonly DecodeFunc<ulong> ULongDecodeFunc =
            iceDecoder => iceDecoder.DecodeULong();

        /// <summary>A <see cref="DecodeFunc{T}"/> for <c>ushort</c> values.</summary>
        public static readonly DecodeFunc<ushort> UShortDecodeFunc =
            iceDecoder => iceDecoder.DecodeUShort();

        /// <summary>A <see cref="DecodeFunc{T}"/> for var int values.</summary>
        public static readonly DecodeFunc<int> VarIntDecodeFunc =
            iceDecoder => iceDecoder.DecodeVarInt();

        /// <summary>A <see cref="DecodeFunc{T}"/> for var long values.</summary>
        public static readonly DecodeFunc<long> VarLongDecodeFunc =
            iceDecoder => iceDecoder.DecodeVarLong();

        /// <summary>A <see cref="DecodeFunc{T}"/> for var uint values.</summary>
        public static readonly DecodeFunc<uint> VarUIntDecodeFunc =
            iceDecoder => iceDecoder.DecodeVarUInt();

        /// <summary>A <see cref="DecodeFunc{T}"/> for var ulong values.</summary>
        public static readonly DecodeFunc<ulong> VarULongDecodeFunc =
            iceDecoder => iceDecoder.DecodeVarULong();
    }
}
