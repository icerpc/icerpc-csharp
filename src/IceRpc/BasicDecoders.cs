// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc
{
    /// <summary>Reads a byte buffer encoded using the Ice encoding.</summary>
    public sealed partial class BufferReader
    {
        // Cached Decoder static objects used by the generated code

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>bool</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<bool> IceReaderIntoBool =
            reader => reader.ReadBool();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>byte</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<byte> IceReaderIntoByte =
            reader => reader.ReadByte();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>double</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<double> IceReaderIntoDouble =
            reader => reader.ReadDouble();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>float</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<float> IceReaderIntoFloat =
            reader => reader.ReadFloat();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>int</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<int> IceReaderIntoInt =
            reader => reader.ReadInt();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>long</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<long> IceReaderIntoLong =
            reader => reader.ReadLong();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>short</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<short> IceReaderIntoShort =
            reader => reader.ReadShort();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>string</c> instances.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<string> IceReaderIntoString =
            reader => reader.ReadString();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>uint</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<uint> IceReaderIntoUInt =
            reader => reader.ReadUInt();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>ulong</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<ulong> IceReaderIntoULong =
            reader => reader.ReadULong();

        /// <summary>A <see cref="Decoder{T}"/> used to read <c>ushort</c> values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<ushort> IceReaderIntoUShort =
            reader => reader.ReadUShort();

        /// <summary>A <see cref="Decoder{T}"/> used to read var int values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<int> IceReaderIntoVarInt =
            reader => reader.ReadVarInt();

        /// <summary>A <see cref="Decoder{T}"/> used to read var long values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<long> IceReaderIntoVarLong =
            reader => reader.ReadVarLong();

        /// <summary>A <see cref="Decoder{T}"/> used to read var uint values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<uint> IceReaderIntoVarUInt =
            reader => reader.ReadVarUInt();

        /// <summary>A <see cref="Decoder{T}"/> used to read var ulong values.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Decoder<ulong> IceReaderIntoVarULong =
            reader => reader.ReadVarULong();
    }
}
