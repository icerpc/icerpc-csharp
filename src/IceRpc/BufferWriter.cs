// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc
{
    /// <summary>Writes data into one or more byte buffers using the Ice encoding.</summary>
    public sealed partial class BufferWriter
    {
        /// <summary>Represents a position in the underlying buffer vector. This position consists of the index of the
        /// buffer in the vector and the offset into that buffer.</summary>
        internal struct Position
        {
            /// <summary>Creates a new position from the buffer and offset values.</summary>
            /// <param name="buffer">The zero based index of the buffer in the buffer vector.</param>
            /// <param name="offset">The offset into the buffer.</param>
            internal Position(int buffer, int offset)
            {
                Buffer = buffer;
                Offset = offset;
            }

            /// <summary>The zero based index of the buffer.</summary>
            internal int Buffer;

            /// <summary>The offset into the buffer.</summary>
            internal int Offset;
        }

        /// <summary>The encoding used when writing to this buffer.</summary>
        /// <value>The encoding.</value>
        public Encoding Encoding { get; }

        /// <summary>The number of bytes that the underlying buffer vector can hold without further allocation.
        /// </summary>
        internal int Capacity { get; private set; }

        /// <summary>Determines the current size of the buffer. This corresponds to the number of bytes already written
        /// using this writer.</summary>
        /// <value>The current size.</value>
        internal int Size { get; private set; }

        // Gets the position for the next write operation.
        internal Position Tail => _tail;

        private const int DefaultBufferSize = 256;

        // The number of bytes we use by default when writing a size on a fixed number of byte with the 2.0 encoding.
        private const int DefaultSizeLength = 4;

        private static readonly System.Text.UTF8Encoding _utf8 = new(false, true);

        private bool OldEncoding => Encoding == Encoding.V11;

        // The current class/exception format, can be either Compact or Sliced.
        private readonly FormatType _classFormat;

        // Data for the class or exception instance that is currently getting marshaled.
        private InstanceData _current;

        // The buffer currently used by write operations. The tail Position always points to this buffer, and the tail
        // offset indicates how much of the buffer has been used.
        private Memory<byte> _currentBuffer;

        // Map of class instance to instance ID, where the instance IDs start at 2.
        //  - Instance ID = 0 means null.
        //  - Instance ID = 1 means the instance is encoded inline afterwards.
        //  - Instance ID > 1 means a reference to a previously encoded instance, found in this map.
        private Dictionary<AnyClass, int>? _instanceMap;

        // All buffers before the tail buffer are fully used.
        private Memory<ReadOnlyMemory<byte>> _bufferVector = Memory<ReadOnlyMemory<byte>>.Empty;

        // The position for the next write operation.
        private Position _tail;

        // Map of type ID string to type ID index.
        // We assign a type ID index (starting with 1) to each type ID we write, in order.
        private Dictionary<string, int>? _typeIdMap;

        // Write methods for basic types

        /// <summary>Writes a boolean to the buffer.</summary>
        /// <param name="v">The boolean to write to the buffer.</param>
        public void WriteBool(bool v) => WriteByte(v ? (byte)1 : (byte)0);

        /// <summary>Writes a byte to the buffer.</summary>
        /// <param name="v">The byte to write.</param>
        public void WriteByte(byte v)
        {
            Expand(1);
            _currentBuffer.Span[_tail.Offset] = v;
            _tail.Offset++;
            Size++;
        }

        /// <summary>Writes a double to the buffer.</summary>
        /// <param name="v">The double to write to the buffer.</param>
        public void WriteDouble(double v) => WriteFixedSizeNumeric(v);

        /// <summary>Writes a float to the buffer.</summary>
        /// <param name="v">The float to write to the buffer.</param>
        public void WriteFloat(float v) => WriteFixedSizeNumeric(v);

        /// <summary>Writes an int to the buffer.</summary>
        /// <param name="v">The int to write to the buffer.</param>
        public void WriteInt(int v) => WriteFixedSizeNumeric(v);

        /// <summary>Writes a long to the buffer.</summary>
        /// <param name="v">The long to write to the buffer.</param>
        public void WriteLong(long v) => WriteFixedSizeNumeric(v);

        /// <summary>Writes a short to the buffer.</summary>
        /// <param name="v">The short to write to the buffer.</param>
        public void WriteShort(short v) => WriteFixedSizeNumeric(v);

        /// <summary>Writes a size to the buffer.</summary>
        /// <param name="v">The size.</param>
        public void WriteSize(int v)
        {
            if (OldEncoding)
            {
                if (v < 255)
                {
                    WriteByte((byte)v);
                }
                else
                {
                    WriteByte(255);
                    WriteInt(v);
                }
            }
            else
            {
                WriteVarULong((ulong)v);
            }
        }

        /// <summary>Writes a string to the buffer.</summary>
        /// <param name="v">The string to write to the buffer.</param>
        public void WriteString(string v)
        {
            if (v.Length == 0)
            {
                WriteSize(0);
            }
            else if (v.Length <= 100)
            {
                Span<byte> data = stackalloc byte[_utf8.GetMaxByteCount(v.Length)];
                int written = _utf8.GetBytes(v, data);
                WriteSize(written);
                WriteByteSpan(data.Slice(0, written));
            }
            else
            {
                byte[] data = _utf8.GetBytes(v);
                WriteSize(data.Length);
                WriteByteSpan(data.AsSpan());
            }
        }

        /// <summary>Writes a uint to the buffer.</summary>
        /// <param name="v">The uint to write to the buffer.</param>
        public void WriteUInt(uint v) => WriteFixedSizeNumeric(v);

        /// <summary>Writes a ulong to the buffer.</summary>
        /// <param name="v">The ulong to write to the buffer.</param>
        public void WriteULong(ulong v) => WriteFixedSizeNumeric(v);

        /// <summary>Writes a ushort to the buffer.</summary>
        /// <param name="v">The ushort to write to the buffer.</param>
        public void WriteUShort(ushort v) => WriteFixedSizeNumeric(v);

        /// <summary>Writes an int to the buffer, using IceRPC's variable-size integer encoding.</summary>
        /// <param name="v">The int to write to the buffer.</param>
        public void WriteVarInt(int v) => WriteVarLong(v);

        /// <summary>Writes a long to the buffer, using IceRPC's variable-size integer encoding, with the minimum number
        /// of bytes required by the encoding.</summary>
        /// <param name="v">The long to write to the buffer. It must be in the range [-2^61..2^61 - 1].</param>
        public void WriteVarLong(long v)
        {
            int encodedSizeExponent = GetVarLongEncodedSizeExponent(v);
            v <<= 2;
            v |= (uint)encodedSizeExponent;
            Span<byte> data = stackalloc byte[sizeof(long)];
            MemoryMarshal.Write(data, ref v);
            WriteByteSpan(data.Slice(0, 1 << encodedSizeExponent));
        }

        /// <summary>Writes a uint to the buffer, using IceRPC's variable-size integer encoding.</summary>
        /// <param name="v">The uint to write to the buffer.</param>
        public void WriteVarUInt(uint v) => WriteVarULong(v);

        /// <summary>Writes a ulong to the buffer, using IceRPC's variable-size integer encoding, with the minimum
        /// number of bytes required by the encoding.</summary>
        /// <param name="v">The ulong to write to the buffer. It must be in the range [0..2^62 - 1].</param>
        public void WriteVarULong(ulong v)
        {
            int encodedSizeExponent = GetVarULongEncodedSizeExponent(v);
            v <<= 2;
            v |= (uint)encodedSizeExponent;
            Span<byte> data = stackalloc byte[sizeof(ulong)];
            MemoryMarshal.Write(data, ref v);
            WriteByteSpan(data.Slice(0, 1 << encodedSizeExponent));
        }

        // Write methods for constructed types except class and exception

        /// <summary>Writes an array of fixed-size numeric values, such as int and long, to the buffer.</summary>
        /// <param name="v">The array of numeric values.</param>
        public void WriteArray<T>(T[] v) where T : struct => WriteSequence(new ReadOnlySpan<T>(v));

        /// <summary>Writes a dictionary to the buffer.</summary>
        /// <param name="v">The dictionary to write.</param>
        /// <param name="keyEncoder">The encoder for the keys.</param>
        /// <param name="valueEncoder">The encoder for the values.</param>
        public void WriteDictionary<TKey, TValue>(
            IEnumerable<KeyValuePair<TKey, TValue>> v,
            Encoder<TKey> keyEncoder,
            Encoder<TValue> valueEncoder)
            where TKey : notnull
        {
            WriteSize(v.Count());
            foreach ((TKey key, TValue value) in v)
            {
                keyEncoder(this, key);
                valueEncoder(this, value);
            }
        }

        /// <summary>Writes a dictionary to the buffer. The dictionary's value type is reference type.</summary>
        /// <param name="v">The dictionary to write.</param>
        /// <param name="withBitSequence">When true, encodes entries with a null value using a bit sequence; otherwise,
        /// false.</param>
        /// <param name="keyEncoder">The encoder for the keys.</param>
        /// <param name="valueEncoder">The encoder for the non-null values.</param>
        public void WriteDictionary<TKey, TValue>(
            IEnumerable<KeyValuePair<TKey, TValue?>> v,
            bool withBitSequence,
            Encoder<TKey> keyEncoder,
            Encoder<TValue> valueEncoder)
            where TKey : notnull
            where TValue : class
        {
            if (withBitSequence)
            {
                int count = v.Count();
                WriteSize(count);
                BitSequence bitSequence = WriteBitSequence(count);
                int index = 0;
                foreach ((TKey key, TValue? value) in v)
                {
                    keyEncoder(this, key);
                    if (value != null)
                    {
                        valueEncoder(this, value);
                    }
                    else
                    {
                        bitSequence[index] = false;
                    }
                    index++;
                }
            }
            else
            {
                WriteDictionary((IEnumerable<KeyValuePair<TKey, TValue>>)v, keyEncoder, valueEncoder);
            }
        }

        /// <summary>Writes a dictionary to the buffer. The dictionary's value type is a nullable value type.</summary>
        /// <param name="v">The dictionary to write.</param>
        /// <param name="keyEncoder">The encoder for the keys.</param>
        /// <param name="valueEncoder">The encoder for the non-null values.</param>
        public void WriteDictionary<TKey, TValue>(
            IEnumerable<KeyValuePair<TKey, TValue?>> v,
            Encoder<TKey> keyEncoder,
            Encoder<TValue> valueEncoder)
            where TKey : notnull
            where TValue : struct
        {
            int count = v.Count();
            WriteSize(count);
            BitSequence bitSequence = WriteBitSequence(count);
            int index = 0;
            foreach ((TKey key, TValue? value) in v)
            {
                keyEncoder(this, key);
                if (value is TValue actualValue)
                {
                    valueEncoder(this, actualValue);
                }
                else
                {
                    bitSequence[index] = false;
                }
                index++;
            }
        }

        /// <summary>Writes a nullable proxy to the buffer.</summary>
        /// <param name="v">The proxy to write, or null.</param>
        public void WriteNullableProxy(IServicePrx? v)
        {
            if (v != null)
            {
                v.IceWrite(this);
            }
            else
            {
                if (OldEncoding)
                {
                    Identity.Empty.IceWrite(this);
                }
                else
                {
                    ProxyData20 nullValue = default;
                    nullValue.IceWrite(this);
                }
            }
        }

        /// <summary>Writes a proxy to the buffer.</summary>
        /// <param name="v">The proxy to write. This proxy cannot be null.</param>
        public void WriteProxy(IServicePrx v) => v.IceWrite(this);

        /// <summary>Writes a sequence of fixed-size numeric values, such as int and long, to the buffer.</summary>
        /// <param name="v">The sequence of numeric values represented by a ReadOnlySpan.</param>
        // This method works because (as long as) there is no padding in the memory representation of the
        // ReadOnlySpan.
        public void WriteSequence<T>(ReadOnlySpan<T> v) where T : struct
        {
            WriteSize(v.Length);
            if (!v.IsEmpty)
            {
                WriteByteSpan(MemoryMarshal.AsBytes(v));
            }
        }

        /// <summary>Writes a sequence of fixed-size numeric values, such as int and long, to the buffer.</summary>
        /// <param name="v">The sequence of numeric values.</param>
        public void WriteSequence<T>(IEnumerable<T> v) where T : struct
        {
            if (v is T[] vArray)
            {
                WriteArray(vArray);
            }
            else if (v is ImmutableArray<T> vImmutableArray)
            {
                WriteSequence(vImmutableArray.AsSpan());
            }
            else
            {
                WriteSequence(v, (writer, element) => writer.WriteFixedSizeNumeric(element));
            }
        }

        /// <summary>Writes a sequence to the buffer.</summary>
        /// <param name="v">The sequence to write.</param>
        /// <param name="encoder">The encoder for an element.</param>
        public void WriteSequence<T>(IEnumerable<T> v, Encoder<T> encoder)
        {
            WriteSize(v.Count()); // potentially slow Linq Count()
            foreach (T item in v)
            {
                encoder(this, item);
            }
        }

        /// <summary>Writes a sequence to the buffer. The elements of the sequence are reference types.</summary>
        /// <param name="v">The sequence to write.</param>
        /// <param name="withBitSequence">True to encode null elements using a bit sequence; otherwise, false.</param>
        /// <param name="encoder">The encoder for a non-null element.</param>
        public void WriteSequence<T>(IEnumerable<T?> v, bool withBitSequence, Encoder<T> encoder)
            where T : class
        {
            if (withBitSequence)
            {
                int count = v.Count(); // potentially slow Linq Count()
                WriteSize(count);
                BitSequence bitSequence = WriteBitSequence(count);
                int index = 0;
                foreach (T? item in v)
                {
                    if (item is T value)
                    {
                        encoder(this, value);
                    }
                    else
                    {
                        bitSequence[index] = false;
                    }
                    index++;
                }
            }
            else
            {
                WriteSequence((IEnumerable<T>)v, encoder);
            }
        }

        /// <summary>Writes a sequence of nullable values to the buffer.</summary>
        /// <param name="v">The sequence to write.</param>
        /// <param name="encoder">The encoder for the non-null values.</param>
        public void WriteSequence<T>(IEnumerable<T?> v, Encoder<T> encoder) where T : struct
        {
            int count = v.Count(); // potentially slow Linq Count()
            WriteSize(count);
            BitSequence bitSequence = WriteBitSequence(count);
            int index = 0;
            foreach (T? item in v)
            {
                if (item is T value)
                {
                    encoder(this, value);
                }
                else
                {
                    bitSequence[index] = false;
                }
                index++;
            }
        }

        /// <summary>Writes a mapped Slice struct to the buffer.</summary>
        /// <param name="v">The struct instance to write.</param>
        public void WriteStruct<T>(T v) where T : struct, IEncodable => v.IceWrite(this);

        // Write methods for tagged basic types

        /// <summary>Writes a tagged boolean to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The boolean to write to the buffer.</param>
        public void WriteTaggedBool(int tag, bool? v)
        {
            if (v is bool value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F1);
                WriteBool(value);
            }
        }

        /// <summary>Writes a tagged byte to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The byte to write to the buffer.</param>
        public void WriteTaggedByte(int tag, byte? v)
        {
            if (v is byte value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F1);
                WriteByte(value);
            }
        }

        /// <summary>Writes a tagged double to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The double to write to the buffer.</param>
        public void WriteTaggedDouble(int tag, double? v)
        {
            if (v is double value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F8);
                WriteDouble(value);
            }
        }

        /// <summary>Writes a tagged float to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The float to write to the buffer.</param>
        public void WriteTaggedFloat(int tag, float? v)
        {
            if (v is float value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F4);
                WriteFloat(value);
            }
        }

        /// <summary>Writes a tagged int to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The int to write to the buffer.</param>
        public void WriteTaggedInt(int tag, int? v)
        {
            if (v is int value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F4);
                WriteInt(value);
            }
        }

        /// <summary>Writes a tagged long to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The long to write to the buffer.</param>
        public void WriteTaggedLong(int tag, long? v)
        {
            if (v is long value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F8);
                WriteLong(value);
            }
        }

        /// <summary>Writes a tagged size to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The size.</param>
        public void WriteTaggedSize(int tag, int? v)
        {
            if (v is int value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.Size);
                WriteSize(value);
            }
        }

        /// <summary>Writes a tagged short to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The short to write to the buffer.</param>
        public void WriteTaggedShort(int tag, short? v)
        {
            if (v is short value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F2);
                WriteShort(value);
            }
        }

        /// <summary>Writes a tagged string to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The string to write to the buffer.</param>
        public void WriteTaggedString(int tag, string? v)
        {
            if (v is string value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize);
                WriteString(value);
            }
        }

        /// <summary>Writes a tagged uint to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The uint to write to the buffer.</param>
        public void WriteTaggedUInt(int tag, uint? v)
        {
            if (v is uint value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F4);
                WriteUInt(value);
            }
        }

        /// <summary>Writes a tagged ulong to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The ulong to write to the buffer.</param>
        public void WriteTaggedULong(int tag, ulong? v)
        {
            if (v is ulong value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F8);
                WriteULong(value);
            }
        }

        /// <summary>Writes a tagged ushort to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The ushort to write to the buffer.</param>
        public void WriteTaggedUShort(int tag, ushort? v)
        {
            if (v is ushort value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F2);
                WriteUShort(value);
            }
        }

        /// <summary>Writes a tagged int to the buffer, using IceRPC's variable-size integer encoding.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The int to write to the buffer.</param>
        public void WriteTaggedVarInt(int tag, int? v) => WriteTaggedVarLong(tag, v);

        /// <summary>Writes a tagged long to the buffer, using IceRPC's variable-size integer encoding.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The long to write to the buffer.</param>
        public void WriteTaggedVarLong(int tag, long? v)
        {
            if (v is long value)
            {
                var format = (EncodingDefinitions.TagFormat)GetVarLongEncodedSizeExponent(value);
                WriteTaggedParamHeader(tag, format);
                WriteVarLong(value);
            }
        }

        /// <summary>Writes a tagged uint to the buffer, using IceRPC's variable-size integer encoding.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The uint to write to the buffer.</param>
        public void WriteTaggedVarUInt(int tag, uint? v) => WriteTaggedVarULong(tag, v);

        /// <summary>Writes a tagged ulong to the buffer, using IceRPC's variable-size integer encoding.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The ulong to write to the buffer.</param>
        public void WriteTaggedVarULong(int tag, ulong? v)
        {
            if (v is ulong value)
            {
                var format = (EncodingDefinitions.TagFormat)GetVarULongEncodedSizeExponent(value);
                WriteTaggedParamHeader(tag, format);
                WriteVarULong(value);
            }
        }

        // Write methods for tagged constructed types except class

        /// <summary>Writes a tagged dictionary with fixed-size entries to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The dictionary to write.</param>
        /// <param name="entrySize">The size of each entry (key + value), in bytes.</param>
        /// <param name="keyEncoder">The encoder for the keys.</param>
        /// <param name="valueEncoder">The encoder for the values.</param>
        public void WriteTaggedDictionary<TKey, TValue>(
            int tag,
            IEnumerable<KeyValuePair<TKey, TValue>>? v,
            int entrySize,
            Encoder<TKey> keyEncoder,
            Encoder<TValue> valueEncoder)
            where TKey : notnull
        {
            Debug.Assert(entrySize > 1);
            if (v is IEnumerable<KeyValuePair<TKey, TValue>> dict)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize);
                int count = dict.Count();
                WriteSize(count == 0 ? 1 : (count * entrySize) + GetSizeLength(count));
                WriteDictionary(dict, keyEncoder, valueEncoder);
            }
        }

        /// <summary>Writes a tagged dictionary with variable-size elements to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The dictionary to write.</param>
        /// <param name="keyEncoder">The encoder for the keys.</param>
        /// <param name="valueEncoder">The encoder for the values.</param>
        public void WriteTaggedDictionary<TKey, TValue>(
            int tag,
            IEnumerable<KeyValuePair<TKey, TValue>>? v,
            Encoder<TKey> keyEncoder,
            Encoder<TValue> valueEncoder)
            where TKey : notnull
        {
            if (v is IEnumerable<KeyValuePair<TKey, TValue>> dict)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                WriteDictionary(dict, keyEncoder, valueEncoder);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Writes a tagged dictionary to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The dictionary to write.</param>
        /// <param name="withBitSequence">When true, encodes entries with a null value using a bit sequence; otherwise,
        /// false.</param>
        /// <param name="keyEncoder">The encoder for the keys.</param>
        /// <param name="valueEncoder">The encoder for the values.</param>
        public void WriteTaggedDictionary<TKey, TValue>(
            int tag,
            IEnumerable<KeyValuePair<TKey, TValue?>>? v,
            bool withBitSequence,
            Encoder<TKey> keyEncoder,
            Encoder<TValue> valueEncoder)
            where TKey : notnull
            where TValue : class
        {
            if (v is IEnumerable<KeyValuePair<TKey, TValue?>> dict)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                WriteDictionary(dict, withBitSequence, keyEncoder, valueEncoder);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Writes a tagged dictionary to the buffer. The dictionary's value type is a nullable value type.
        /// </summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The dictionary to write.</param>
        /// <param name="keyEncoder">The encoder for the keys.</param>
        /// <param name="valueEncoder">The encoder for the non-null values.</param>
        public void WriteTaggedDictionary<TKey, TValue>(
            int tag,
            IEnumerable<KeyValuePair<TKey, TValue?>>? v,
            Encoder<TKey> keyEncoder,
            Encoder<TValue> valueEncoder)
            where TKey : notnull
            where TValue : struct
        {
            if (v is IEnumerable<KeyValuePair<TKey, TValue?>> dict)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                WriteDictionary(dict, keyEncoder, valueEncoder);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Writes a tagged proxy to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The proxy to write.</param>
        public void WriteTaggedProxy(int tag, IServicePrx? v)
        {
            if (v != null)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                WriteProxy(v);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Writes a tagged sequence of fixed-size numeric values to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to write.</param>
        public void WriteTaggedSequence<T>(int tag, ReadOnlySpan<T> v) where T : struct
        {
            // A null T[]? or List<T>? is implicitly converted into a default aka null ReadOnlyMemory<T> or
            // ReadOnlySpan<T>. Furthermore, the span of a default ReadOnlyMemory<T> is a default ReadOnlySpan<T>, which
            // is distinct from the span of an empty sequence. This is why the "v != null" below works correctly.
            if (v != null)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize);
                int elementSize = Unsafe.SizeOf<T>();
                if (elementSize > 1)
                {
                    // This size is redundant and optimized out by the encoding when elementSize is 1.
                    WriteSize(v.IsEmpty ? 1 : (v.Length * elementSize) + GetSizeLength(v.Length));
                }
                WriteSequence(v);
            }
        }

        /// <summary>Writes a tagged sequence of fixed-size numeric values to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to write.</param>
        public void WriteTaggedSequence<T>(int tag, IEnumerable<T>? v) where T : struct
        {
            if (v is IEnumerable<T> value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize);

                int elementSize = Unsafe.SizeOf<T>();
                if (elementSize > 1)
                {
                    int count = value.Count(); // potentially slow Linq Count()

                    // First write the size in bytes, so that the decoder can skip it. We optimize-out this byte size
                    // when elementSize is 1.
                    WriteSize(count == 0 ? 1 : (count * elementSize) + GetSizeLength(count));
                }
                WriteSequence(value);
            }
        }

        /// <summary>Writes a tagged sequence of variable-size elements to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to write.</param>
        /// <param name="encoder">The encoder for an element.</param>
        public void WriteTaggedSequence<T>(int tag, IEnumerable<T>? v, Encoder<T> encoder)
        {
            if (v is IEnumerable<T> value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                WriteSequence(value, encoder);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Writes a tagged sequence of fixed-size values to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to write.</param>
        /// <param name="elementSize">The fixed size of each element of the sequence, in bytes.</param>
        /// <param name="encoder">The encoder for an element.</param>
        public void WriteTaggedSequence<T>(int tag, IEnumerable<T>? v, int elementSize, Encoder<T> encoder)
            where T : struct
        {
            Debug.Assert(elementSize > 0);
            if (v is IEnumerable<T> value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize);

                int count = value.Count(); // potentially slow Linq Count()

                if (elementSize > 1)
                {
                    // First write the size in bytes, so that the decoder can skip it. We optimize-out this byte size
                    // when elementSize is 1.
                    WriteSize(count == 0 ? 1 : (count * elementSize) + GetSizeLength(count));
                }
                WriteSize(count);
                foreach (T item in value)
                {
                    encoder(this, item);
                }
            }
        }

        /// <summary>Writes a tagged sequence of nullable elements to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to write.</param>
        /// <param name="withBitSequence">True to encode null elements using a bit sequence; otherwise, false.</param>
        /// <param name="encoder">The encoder for a non-null element.</param>
        public void WriteTaggedSequence<T>(
            int tag,
            IEnumerable<T?>? v,
            bool withBitSequence,
            Encoder<T> encoder)
            where T : class
        {
            if (v is IEnumerable<T?> value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                WriteSequence(value, withBitSequence, encoder);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Writes a tagged sequence of nullable values to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to write.</param>
        /// <param name="encoder">The encoder for a non-null element.</param>
        public void WriteTaggedSequence<T>(int tag, IEnumerable<T?>? v, Encoder<T> encoder)
            where T : struct
        {
            if (v is IEnumerable<T?> value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                WriteSequence(value, encoder);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Writes a tagged fixed-size struct to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The struct to write.</param>
        /// <param name="fixedSize">The size of the struct, in bytes.</param>
        public void WriteTaggedStruct<T>(int tag, T? v, int fixedSize) where T : struct, IEncodable
        {
            if (v is T value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize);
                WriteSize(fixedSize);
                value.IceWrite(this);
            }
        }

        /// <summary>Writes a tagged variable-size struct to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The struct to write.</param>
        public void WriteTaggedStruct<T>(int tag, T? v) where T : struct, IEncodable
        {
            if (v is T value)
            {
                WriteTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                value.IceWrite(this);
                EndFixedLengthSize(pos);
            }
        }

        // Other methods

        /// <summary>Writes a sequence of bits to the buffer, and returns this sequence backed by the buffer.</summary>
        /// <param name="bitSize">The minimum number of bits in the sequence.</param>
        /// <returns>The bit sequence, with all bits set. The actual size of the sequence is a multiple of 8.
        /// </returns>
        public BitSequence WriteBitSequence(int bitSize)
        {
            Debug.Assert(bitSize > 0);
            int size = (bitSize >> 3) + ((bitSize & 0x07) != 0 ? 1 : 0);

            Expand(size);

            int remaining = _currentBuffer.Length - _tail.Offset;
            if (size <= remaining)
            {
                // Expand above ensures _tail.Offset is not _currentBuffer.Count.
                Span<byte> span = _currentBuffer.Span.Slice(_tail.Offset, size);
                span.Fill(255);
                _tail.Offset += size;
                Size += size;
                return new BitSequence(span);
            }
            else
            {
                Span<byte> firstSpan = _currentBuffer.Span[_tail.Offset..];
                firstSpan.Fill(255);
                _currentBuffer = GetBuffer(++_tail.Buffer);
                _tail.Offset = size - remaining;
                Size += size;
                Span<byte> secondSpan = _currentBuffer.Span.Slice(0, _tail.Offset);
                secondSpan.Fill(255);
                return new BitSequence(firstSpan, secondSpan);
            }
        }

        /// <summary>Computes the minimum number of bytes needed to write a variable-length size with the 2.0 encoding.
        /// </summary>
        /// <remarks>The parameter is a long and not a varulong because sizes and size-like values are usually passed
        /// around as signed integers, even though sizes cannot be negative and are encoded like varulong values.
        /// </remarks>
        internal static int GetSizeLength20(long size)
        {
            Debug.Assert(size >= 0);
            return 1 << GetVarULongEncodedSizeExponent((ulong)size);
        }

        // Constructs a buffer writer
        internal BufferWriter(Encoding encoding, Memory<byte> initialBuffer = default, FormatType classFormat = default)
        {
            Encoding = encoding;
            Encoding.CheckSupported();
            _classFormat = classFormat;
            _tail = default;
            Size = 0;
            _currentBuffer = initialBuffer;
            if (_currentBuffer.Length > 0)
            {
                _bufferVector = new ReadOnlyMemory<byte>[] { _currentBuffer };
            }

            Capacity = _currentBuffer.Length;
        }

        /// <summary>Computes the amount of data written from the start position to the current position and writes that
        /// size at the start position (as a fixed-length size). The size does not include its own encoded length.
        /// </summary>
        /// <param name="start">The start position.</param>
        /// <param name="sizeLength">The number of bytes used to marshal the size 1, 2 or 4.</param>
        internal void EndFixedLengthSize(Position start, int sizeLength = DefaultSizeLength)
        {
            Debug.Assert(start.Offset >= 0);
            if (OldEncoding)
            {
                RewriteFixedLengthSize11(Distance(start) - 4, start);
            }
            else
            {
                RewriteFixedLengthSize20(Distance(start) - sizeLength, start, sizeLength);
            }
        }

        /// <summary>Finishes off the underlying buffer vector and returns it. You should not write additional data to
        /// this buffer writer after calling Finish, however rewriting previous data (with for example
        /// <see cref="EndFixedLengthSize"/>) is fine.</summary>
        /// <returns>The buffers.</returns>
        internal ReadOnlyMemory<ReadOnlyMemory<byte>> Finish()
        {
            _bufferVector.Span[^1] = _bufferVector.Span[^1].Slice(0, _tail.Offset);
            return _bufferVector;
        }

        /// <summary>Writes a size on a fixed number of bytes at the given position.</summary>
        /// <param name="size">The size to write.</param>
        /// <param name="pos">The position to write to.</param>
        /// <param name="sizeLength">The number of bytes used to encode the size. Can be 1, 2 or 4.</param>
        internal void RewriteFixedLengthSize20(int size, Position pos, int sizeLength = DefaultSizeLength)
        {
            Debug.Assert(pos.Buffer < _bufferVector.Length);
            Debug.Assert(sizeLength == 1 || sizeLength == 2 || sizeLength == 4);

            Span<byte> data = stackalloc byte[sizeLength];
            data.WriteFixedLengthSize20(size);
            RewriteByteSpan(data, pos);
        }

        /// <summary>Returns the current position and writes placeholder for a fixed-length size value. The
        /// position must be used to rewrite the size later.</summary>
        /// <param name="sizeLength">The number of bytes reserved to write the fixed-length size.</param>
        /// <returns>The position before writing the size.</returns>
        internal Position StartFixedLengthSize(int sizeLength = DefaultSizeLength)
        {
            Position pos = _tail;
            WriteByteSpan(stackalloc byte[OldEncoding ? 4 : sizeLength]); // placeholder for future size
            return pos;
        }

        /// <summary>Writes a span of bytes. The writer capacity is expanded if required, the size and tail position are
        /// increased according to the span length.</summary>
        /// <param name="span">The data to write as a span of bytes.</param>
        internal void WriteByteSpan(ReadOnlySpan<byte> span)
        {
            int length = span.Length;
            if (length > 0)
            {
                Expand(length);
                Size += length;
                int offset = _tail.Offset;
                int remaining = _currentBuffer.Length - offset;
                Debug.Assert(remaining > 0); // guaranteed by Expand
                int sz = Math.Min(length, remaining);
                if (length > remaining)
                {
                    span.Slice(0, remaining).CopyTo(_currentBuffer.Span.Slice(offset, sz));
                }
                else
                {
                    span.CopyTo(_currentBuffer.Span.Slice(offset, length));
                }
                _tail.Offset += sz;
                length -= sz;

                if (length > 0)
                {
                    _currentBuffer = GetBuffer(++_tail.Buffer);
                    if (remaining == 0)
                    {
                        span.CopyTo(_currentBuffer.Span.Slice(0, length));
                    }
                    else
                    {
                        span.Slice(remaining, length).CopyTo(_currentBuffer.Span.Slice(0, length));
                    }
                    _tail.Offset = length;
                }
            }
        }

        internal void WriteEndpoint11(Endpoint endpoint)
        {
            Debug.Assert(OldEncoding);

            this.Write(endpoint.Transport);
            Position startPos = _tail;

            WriteInt(0); // placeholder for future encapsulation size
            if (endpoint is OpaqueEndpoint opaqueEndpoint)
            {
                opaqueEndpoint.ValueEncoding.IceWrite(this);
                WriteByteSpan(opaqueEndpoint.Value.Span); // WriteByteSpan is not encoding-sensitive
            }
            else
            {
                Encoding.IceWrite(this);
                if (endpoint.Protocol == Protocol.Ice1)
                {
                    endpoint.WriteOptions11(this);
                }
                else
                {
                    WriteString(endpoint.Data.Host);
                    WriteUShort(endpoint.Data.Port);
                    WriteSequence(endpoint.Data.Options, BasicEncoders.StringEncoder);
                }
            }
            RewriteFixedLengthSize11(Distance(startPos), startPos);
        }

        internal void WriteField(int key, ReadOnlySpan<byte> value)
        {
            WriteVarInt(key);
            WriteSize(value.Length);
            WriteByteSpan(value);
        }

        internal void WriteField<T>(int key, T value, Encoder<T> encoder)
        {
            WriteVarInt(key);
            Position pos = StartFixedLengthSize(2); // 2-bytes size place holder
            encoder(this, value);
            EndFixedLengthSize(pos, 2);
        }

        private static int Distance(ReadOnlyMemory<ReadOnlyMemory<byte>> data, Position start, Position end)
        {
            // If both the start and end position are in the same array buffer just
            // compute the offsets distance.
            if (start.Buffer == end.Buffer)
            {
                return end.Offset - start.Offset;
            }

            // If start and end position are in different buffers we need to accumulate the
            // size from start offset to the end of the start buffer, the size of the intermediary
            // buffers, and the current offset into the last buffer.
            ReadOnlyMemory<byte> buffer = data.Span[start.Buffer];
            int size = buffer.Length - start.Offset;
            for (int i = start.Buffer + 1; i < end.Buffer; ++i)
            {
                checked
                {
                    size += data.Span[i].Length;
                }
            }
            checked
            {
                return size + end.Offset;
            }
        }

        /// <summary>Gets the minimum number of bytes needed to encode a long value with the varlong encoding as an
        /// exponent of 2.</summary>
        /// <param name="value">The value to encode.</param>
        /// <returns>N where 2^N is the number of bytes needed to encode value with IceRPC's varlong encoding.</returns>
        private static int GetVarLongEncodedSizeExponent(long value)
        {
            if (value < EncodingDefinitions.VarLongMinValue || value > EncodingDefinitions.VarLongMaxValue)
            {
                throw new ArgumentOutOfRangeException($"varlong value '{value}' is out of range", nameof(value));
            }

            return (value << 2) switch
            {
                long b when b >= sbyte.MinValue && b <= sbyte.MaxValue => 0,
                long s when s >= short.MinValue && s <= short.MaxValue => 1,
                long i when i >= int.MinValue && i <= int.MaxValue => 2,
                _ => 3
            };
        }

        /// <summary>Gets the mimimum number of bytes needed to encode a long value with the varulong encoding as an
        /// exponent of 2.</summary>
        /// <param name="value">The value to encode.</param>
        /// <returns>N where 2^N is the number of bytes needed to encode value with varulong encoding.</returns>
        private static int GetVarULongEncodedSizeExponent(ulong value)
        {
            if (value > EncodingDefinitions.VarULongMaxValue)
            {
                throw new ArgumentOutOfRangeException($"varulong value '{value}' is out of range", nameof(value));
            }

            return (value << 2) switch
            {
                ulong b when b <= byte.MaxValue => 0,
                ulong s when s <= ushort.MaxValue => 1,
                ulong i when i <= uint.MaxValue => 2,
                _ => 3
            };
        }

        /// <summary>Returns the distance in bytes from start position to the current position.</summary>
        /// <param name="start">The start position from where to calculate distance to current position.</param>
        /// <returns>The distance in bytes from the current position to the start position.</returns>
        private int Distance(Position start)
        {
            Debug.Assert(_tail.Buffer > start.Buffer ||
                        (_tail.Buffer == start.Buffer && _tail.Offset >= start.Offset));

            return Distance(_bufferVector, start, _tail);
        }

        /// <summary>Expands the writer's buffer to make room for more data. If the bytes remaining in the buffer are
        /// not enough to hold the given number of bytes, allocates a new byte array. The caller should then consume the
        /// new bytes immediately; calling Expand repeatedly is not supported.</summary>
        /// <param name="n">The number of bytes to accommodate in the buffer.</param>
        private void Expand(int n)
        {
            Debug.Assert(n > 0);
            int remaining = Capacity - Size;
            if (n > remaining)
            {
                int size = Math.Max(DefaultBufferSize, _currentBuffer.Length * 2);
                size = Math.Max(n - remaining, size);
                byte[] buffer = new byte[size];

                if (_bufferVector.Length == 0)
                {
                    // First Expand for a new buffer writer constructed with no buffer.
                    Debug.Assert(_currentBuffer.Length == 0);
                    _bufferVector = new ReadOnlyMemory<byte>[] { buffer };
                    _currentBuffer = buffer;
                }
                else
                {
                    var newBufferVector = new ReadOnlyMemory<byte>[_bufferVector.Length + 1];
                    _bufferVector.CopyTo(newBufferVector.AsMemory());
                    newBufferVector[^1] = buffer;
                    _bufferVector = newBufferVector;

                    if (remaining == 0)
                    {
                        // Patch _tail to point to the first byte in the new buffer.
                        Debug.Assert(_tail.Offset == _currentBuffer.Length);
                        _currentBuffer = buffer;
                        _tail.Buffer++;
                        _tail.Offset = 0;
                    }
                }
                Capacity += buffer.Length;
            }

            // Once Expand returns, _tail points to a writeable byte.
            Debug.Assert(_tail.Offset < _currentBuffer.Length);
        }

        /// <summary>Returns the buffer at the given index.</summary>
        private Memory<byte> GetBuffer(int index) => MemoryMarshal.AsMemory(_bufferVector.Span[index]);

        /// <summary>Computes the minimum number of bytes needed to write a variable-length size with the current
        /// encoding.</summary>
        /// <param name="size">The size.</param>
        /// <returns>The minimum number of bytes.</returns>
        private int GetSizeLength(int size) => OldEncoding ? (size < 255 ? 1 : 5) : GetSizeLength20(size);

        /// <summary>Writes a byte at a given position.</summary>
        /// <param name="v">The byte value to write.</param>
        /// <param name="pos">The position to write to.</param>
        private void RewriteByte(byte v, Position pos)
        {
            Memory<byte> buffer = GetBuffer(pos.Buffer);

            if (pos.Offset < buffer.Length)
            {
                buffer.Span[pos.Offset] = v;
            }
            else
            {
                // (segN, segN.Count) points to the same byte as (segN + 1, 0)
                Debug.Assert(pos.Offset == buffer.Length);
                buffer = GetBuffer(pos.Buffer + 1);
                buffer.Span[0] = v;
            }
        }

        /// <summary>Writes a size on 4 bytes at the given position.</summary>
        /// <param name="size">The size to write.</param>
        /// <param name="pos">The position to write to.</param>
        internal void RewriteFixedLengthSize11(int size, Position pos)
        {
            Debug.Assert(pos.Buffer < _bufferVector.Length);

            Span<byte> data = stackalloc byte[4];
            MemoryMarshal.Write(data, ref size);
            RewriteByteSpan(data, pos);
        }

        private void RewriteByteSpan(Span<byte> data, Position pos)
        {
            Memory<byte> buffer = GetBuffer(pos.Buffer);

            int remaining = Math.Min(data.Length, buffer.Length - pos.Offset);
            if (remaining > 0)
            {
                data.Slice(0, remaining).CopyTo(buffer.Span.Slice(pos.Offset, remaining));
            }

            if (remaining < data.Length)
            {
                buffer = GetBuffer(pos.Buffer + 1);
                data[remaining..].CopyTo(buffer.Span.Slice(0, data.Length - remaining));
            }
        }

        /// <summary>Writes a fixed-size numeric value to the buffer.</summary>
        /// <param name="v">The numeric value to write to the buffer.</param>
        private void WriteFixedSizeNumeric<T>(T v) where T : struct
        {
            int elementSize = Unsafe.SizeOf<T>();
            Span<byte> data = stackalloc byte[elementSize];
            MemoryMarshal.Write(data, ref v);
            WriteByteSpan(data);
        }

        /// <summary>Writes the header for a tagged parameter or data member.</summary>
        /// <param name="tag">The numeric tag associated with the parameter or data member.</param>
        /// <param name="format">The tag format.</param>
        private void WriteTaggedParamHeader(int tag, EncodingDefinitions.TagFormat format)
        {
            Debug.Assert(format != EncodingDefinitions.TagFormat.VInt); // VInt cannot be marshaled

            int v = (int)format;
            if (tag < 30)
            {
                v |= tag << 3;
                WriteByte((byte)v);
            }
            else
            {
                v |= 0x0F0; // tag = 30
                WriteByte((byte)v);
                WriteSize(tag);
            }
            if (_current.InstanceType != InstanceType.None)
            {
                _current.SliceFlags |= EncodingDefinitions.SliceFlags.HasTaggedMembers;
            }
        }
    }
}
