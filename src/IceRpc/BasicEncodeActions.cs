// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Provides encode actions for all the basic types.</summary>
    public static class BasicEncodeActions
    {
        /// <summary>An <see cref="EncodeAction{T}"/> for <c>bool</c> values.</summary>
        public static readonly EncodeAction<bool> BoolEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeBool(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>byte</c> values.</summary>
        public static readonly EncodeAction<byte> ByteEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeByte(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>double</c> values.</summary>
        public static readonly EncodeAction<double> DoubleEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeDouble(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>float</c> values.</summary>
        public static readonly EncodeAction<float> FloatEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeFloat(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>int</c> values.</summary>
        public static readonly EncodeAction<int> IntEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeInt(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>long</c> values.</summary>
        public static readonly EncodeAction<long> LongEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeLong(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>short</c> values.</summary>
        public static readonly EncodeAction<short> ShortEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeShort(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>string</c> instances.</summary>
        public static readonly EncodeAction<string> StringEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeString(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>uint</c> values.</summary>
        public static readonly EncodeAction<uint> UIntEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeUInt(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>ulong</c> values.</summary>
        public static readonly EncodeAction<ulong> ULongEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeULong(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for <c>ushort</c> values.</summary>
        public static readonly EncodeAction<ushort> UShortEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeUShort(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for var int values.</summary>
        public static readonly EncodeAction<int> VarIntEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeVarInt(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for var long values.</summary>
        public static readonly EncodeAction<long> VarLongEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeVarLong(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for var uint values.</summary>
        public static readonly EncodeAction<uint> VarUIntEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeVarUInt(value);

        /// <summary>An <see cref="EncodeAction{T}"/> for var ulong values.</summary>
        public static readonly EncodeAction<ulong> VarULongEncodeAction =
            (iceEncoder, value) => iceEncoder.EncodeVarULong(value);
    }
}
