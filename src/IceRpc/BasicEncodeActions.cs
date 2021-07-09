// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Provides encode actions for all the basic types.</summary>
    public static class BasicEncodeActions
    {
        /// <summary>An <see cref="EncodeAction{T}"/> for <c>bool</c> values.</summary>
        public static readonly EncodeAction<bool> BoolEncodeAction =
            (encoder, value) => encoder.EncodeBool(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>byte</c> values.</summary>
        public static readonly EncodeAction<byte> ByteEncodeAction =
            (encoder, value) => encoder.EncodeByte(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>double</c> values.</summary>
        public static readonly EncodeAction<double> DoubleEncodeAction =
            (encoder, value) => encoder.EncodeDouble(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>float</c> values.</summary>
        public static readonly EncodeAction<float> FloatEncodeAction =
            (encoder, value) => encoder.EncodeFloat(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>int</c> values.</summary>
        public static readonly EncodeAction<int> IntEncodeAction =
            (encoder, value) => encoder.EncodeInt(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>long</c> values.</summary>
        public static readonly EncodeAction<long> LongEncodeAction =
            (encoder, value) => encoder.EncodeLong(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>short</c> values.</summary>
        public static readonly EncodeAction<short> ShortEncodeAction =
            (encoder, value) => encoder.EncodeShort(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>string</c> instances.</summary>
        public static readonly EncodeAction<string> StringEncodeAction =
            (encoder, value) => encoder.EncodeString(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>uint</c> values.</summary>
        public static readonly EncodeAction<uint> UIntEncodeAction =
            (encoder, value) => encoder.EncodeUInt(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>ulong</c> values.</summary>
        public static readonly EncodeAction<ulong> ULongEncodeAction =
            (encoder, value) => encoder.EncodeULong(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>ushort</c> values.</summary>
        public static readonly EncodeAction<ushort> UShortEncodeAction =
            (encoder, value) => encoder.EncodeUShort(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for var int values.</summary>
        public static readonly EncodeAction<int> VarIntEncodeAction =
            (encoder, value) => encoder.EncodeVarInt(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for var long values.</summary>
        public static readonly EncodeAction<long> VarLongEncodeAction =
            (encoder, value) => encoder.EncodeVarLong(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for var uint values.</summary>
        public static readonly EncodeAction<uint> VarUIntEncodeAction =
            (encoder, value) => encoder.EncodeVarUInt(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for var ulong values.</summary>
        public static readonly EncodeAction<ulong> VarULongEncodeAction =
            (encoder, value) => encoder.EncodeVarULong(value);
    }
}
