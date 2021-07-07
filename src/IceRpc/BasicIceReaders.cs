// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Provides Ice readers for all the basic types.</summary>
    public static class BasicIceReaders
    {
        /// <summary>A <see cref="IceReader{T}"/> for <c>bool</c> values.</summary>
        public static readonly IceReader<bool> BoolIceReader =
            iceDecoder => iceDecoder.ReadBool();

        /// <summary>A <see cref="IceReader{T}"/> for <c>byte</c> values.</summary>
        public static readonly IceReader<byte> ByteIceReader =
            iceDecoder => iceDecoder.ReadByte();

        /// <summary>A <see cref="IceReader{T}"/> for <c>double</c> values.</summary>
        public static readonly IceReader<double> DoubleIceReader =
            iceDecoder => iceDecoder.ReadDouble();

        /// <summary>A <see cref="IceReader{T}"/> for <c>float</c> values.</summary>
        public static readonly IceReader<float> FloatIceReader =
            iceDecoder => iceDecoder.ReadFloat();

        /// <summary>A <see cref="IceReader{T}"/> for <c>int</c> values.</summary>
        public static readonly IceReader<int> IntIceReader =
            iceDecoder => iceDecoder.ReadInt();

        /// <summary>A <see cref="IceReader{T}"/> for <c>long</c> values.</summary>
        public static readonly IceReader<long> LongIceReader =
            iceDecoder => iceDecoder.ReadLong();

        /// <summary>A <see cref="IceReader{T}"/> for <c>short</c> values.</summary>
        public static readonly IceReader<short> ShortIceReader =
            iceDecoder => iceDecoder.ReadShort();

        /// <summary>A <see cref="IceReader{T}"/> for <c>string</c> instances.</summary>
        public static readonly IceReader<string> StringIceReader =
            iceDecoder => iceDecoder.ReadString();

        /// <summary>A <see cref="IceReader{T}"/> for <c>uint</c> values.</summary>
        public static readonly IceReader<uint> UIntIceReader =
            iceDecoder => iceDecoder.ReadUInt();

        /// <summary>A <see cref="IceReader{T}"/> for <c>ulong</c> values.</summary>
        public static readonly IceReader<ulong> ULongIceReader =
            iceDecoder => iceDecoder.ReadULong();

        /// <summary>A <see cref="IceReader{T}"/> for <c>ushort</c> values.</summary>
        public static readonly IceReader<ushort> UShortIceReader =
            iceDecoder => iceDecoder.ReadUShort();

        /// <summary>A <see cref="IceReader{T}"/> for var int values.</summary>
        public static readonly IceReader<int> VarIntIceReader =
            iceDecoder => iceDecoder.ReadVarInt();

        /// <summary>A <see cref="IceReader{T}"/> for var long values.</summary>
        public static readonly IceReader<long> VarLongIceReader =
            iceDecoder => iceDecoder.ReadVarLong();

        /// <summary>A <see cref="IceReader{T}"/> for var uint values.</summary>
        public static readonly IceReader<uint> VarUIntIceReader =
            iceDecoder => iceDecoder.ReadVarUInt();

        /// <summary>A <see cref="IceReader{T}"/> for var ulong values.</summary>
        public static readonly IceReader<ulong> VarULongIceReader =
            iceDecoder => iceDecoder.ReadVarULong();
    }
}
