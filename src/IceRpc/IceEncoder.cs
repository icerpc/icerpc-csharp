// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc
{
    /// <summary>Encodes data into one or more byte buffers using the Ice encoding.</summary>
    public sealed partial class IceEncoder
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

        /// <summary>Determines the current size of the buffer. This corresponds to the number of bytes already encoded
        /// using this encoder.</summary>
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

        // Encode methods for basic types

        /// <summary>Encodes a boolean to the buffer.</summary>
        /// <param name="v">The boolean to encode to the buffer.</param>
        public void EncodeBool(bool v) => EncodeByte(v ? (byte)1 : (byte)0);

        /// <summary>Encodes a byte to the buffer.</summary>
        /// <param name="v">The byte to encode.</param>
        public void EncodeByte(byte v)
        {
            Expand(1);
            _currentBuffer.Span[_tail.Offset] = v;
            _tail.Offset++;
            Size++;
        }

        /// <summary>Encodes a double to the buffer.</summary>
        /// <param name="v">The double to encode to the buffer.</param>
        public void EncodeDouble(double v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a float to the buffer.</summary>
        /// <param name="v">The float to encode to the buffer.</param>
        public void EncodeFloat(float v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes an int to the buffer.</summary>
        /// <param name="v">The int to encode to the buffer.</param>
        public void EncodeInt(int v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a long to the buffer.</summary>
        /// <param name="v">The long to encode to the buffer.</param>
        public void EncodeLong(long v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a short to the buffer.</summary>
        /// <param name="v">The short to encode to the buffer.</param>
        public void EncodeShort(short v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a size to the buffer.</summary>
        /// <param name="v">The size.</param>
        public void EncodeSize(int v)
        {
            if (OldEncoding)
            {
                if (v < 255)
                {
                    EncodeByte((byte)v);
                }
                else
                {
                    EncodeByte(255);
                    EncodeInt(v);
                }
            }
            else
            {
                EncodeVarULong((ulong)v);
            }
        }

        /// <summary>Encodes a string to the buffer.</summary>
        /// <param name="v">The string to encode to the buffer.</param>
        public void EncodeString(string v)
        {
            if (v.Length == 0)
            {
                EncodeSize(0);
            }
            else if (v.Length <= 100)
            {
                Span<byte> data = stackalloc byte[_utf8.GetMaxByteCount(v.Length)];
                int encoded = _utf8.GetBytes(v, data);
                EncodeSize(encoded);
                WriteByteSpan(data.Slice(0, encoded));
            }
            else
            {
                byte[] data = _utf8.GetBytes(v);
                EncodeSize(data.Length);
                WriteByteSpan(data.AsSpan());
            }
        }

        /// <summary>Encodes a uint to the buffer.</summary>
        /// <param name="v">The uint to encode to the buffer.</param>
        public void EncodeUInt(uint v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a ulong to the buffer.</summary>
        /// <param name="v">The ulong to encode to the buffer.</param>
        public void EncodeULong(ulong v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a ushort to the buffer.</summary>
        /// <param name="v">The ushort to encode to the buffer.</param>
        public void EncodeUShort(ushort v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes an int to the buffer, using IceRPC's variable-size integer encoding.</summary>
        /// <param name="v">The int to encode to the buffer.</param>
        public void EncodeVarInt(int v) => EncodeVarLong(v);

        /// <summary>Encodes a long to the buffer, using IceRPC's variable-size integer encoding, with the minimum number
        /// of bytes required by the encoding.</summary>
        /// <param name="v">The long to encode to the buffer. It must be in the range [-2^61..2^61 - 1].</param>
        public void EncodeVarLong(long v)
        {
            int encodedSizeExponent = GetVarLongEncodedSizeExponent(v);
            v <<= 2;
            v |= (uint)encodedSizeExponent;
            Span<byte> data = stackalloc byte[sizeof(long)];
            MemoryMarshal.Write(data, ref v);
            WriteByteSpan(data.Slice(0, 1 << encodedSizeExponent));
        }

        /// <summary>Encodes a uint to the buffer, using IceRPC's variable-size integer encoding.</summary>
        /// <param name="v">The uint to encode to the buffer.</param>
        public void EncodeVarUInt(uint v) => EncodeVarULong(v);

        /// <summary>Encodes a ulong to the buffer, using IceRPC's variable-size integer encoding, with the minimum
        /// number of bytes required by the encoding.</summary>
        /// <param name="v">The ulong to encode to the buffer. It must be in the range [0..2^62 - 1].</param>
        public void EncodeVarULong(ulong v)
        {
            int encodedSizeExponent = GetVarULongEncodedSizeExponent(v);
            v <<= 2;
            v |= (uint)encodedSizeExponent;
            Span<byte> data = stackalloc byte[sizeof(ulong)];
            MemoryMarshal.Write(data, ref v);
            WriteByteSpan(data.Slice(0, 1 << encodedSizeExponent));
        }

        // Encode methods for constructed types except class and exception

        /// <summary>Encodes an array of fixed-size numeric values, such as int and long, to the buffer.</summary>
        /// <param name="v">The array of numeric values.</param>
        public void EncodeArray<T>(T[] v) where T : struct => EncodeSequence(new ReadOnlySpan<T>(v));

        /// <summary>Encodes a dictionary to the buffer.</summary>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the values.</param>
        public void EncodeDictionary<TKey, TValue>(
            IEnumerable<KeyValuePair<TKey, TValue>> v,
            EncodeAction<TKey> keyEncodeAction,
            EncodeAction<TValue> valueEncodeAction)
            where TKey : notnull
        {
            EncodeSize(v.Count());
            foreach ((TKey key, TValue value) in v)
            {
                keyEncodeAction(this, key);
                valueEncodeAction(this, value);
            }
        }

        /// <summary>Encodes a dictionary to the buffer. The dictionary's value type is reference type.</summary>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="withBitSequence">When true, encodes entries with a null value using a bit sequence; otherwise,
        /// false.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the non-null values.</param>
        public void EncodeDictionary<TKey, TValue>(
            IEnumerable<KeyValuePair<TKey, TValue?>> v,
            bool withBitSequence,
            EncodeAction<TKey> keyEncodeAction,
            EncodeAction<TValue> valueEncodeAction)
            where TKey : notnull
            where TValue : class
        {
            if (withBitSequence)
            {
                int count = v.Count();
                EncodeSize(count);
                BitSequence bitSequence = EncodeBitSequence(count);
                int index = 0;
                foreach ((TKey key, TValue? value) in v)
                {
                    keyEncodeAction(this, key);
                    if (value != null)
                    {
                        valueEncodeAction(this, value);
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
                EncodeDictionary((IEnumerable<KeyValuePair<TKey, TValue>>)v, keyEncodeAction, valueEncodeAction);
            }
        }

        /// <summary>Encodes a dictionary to the buffer. The dictionary's value type is a nullable value type.</summary>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the non-null values.</param>
        public void EncodeDictionary<TKey, TValue>(
            IEnumerable<KeyValuePair<TKey, TValue?>> v,
            EncodeAction<TKey> keyEncodeAction,
            EncodeAction<TValue> valueEncodeAction)
            where TKey : notnull
            where TValue : struct
        {
            int count = v.Count();
            EncodeSize(count);
            BitSequence bitSequence = EncodeBitSequence(count);
            int index = 0;
            foreach ((TKey key, TValue? value) in v)
            {
                keyEncodeAction(this, key);
                if (value is TValue actualValue)
                {
                    valueEncodeAction(this, actualValue);
                }
                else
                {
                    bitSequence[index] = false;
                }
                index++;
            }
        }

        /// <summary>Encodes a nullable proxy.</summary>
        /// <param name="proxy">The proxy to encode, or null.</param>
        public void EncodeNullableProxy(Proxy? proxy)
        {
            if (proxy != null)
            {
                EncodeProxy(proxy);
            }
            else
            {
                if (OldEncoding)
                {
                    Identity.Empty.IceEncode(this);
                }
                else
                {
                    ProxyData20 nullValue = default;
                    nullValue.IceEncode(this);
                }
            }
        }

        /// <summary>Encodes a proxy.</summary>
        /// <param name="proxy">The proxy to encode.</param>
        public void EncodeProxy(Proxy proxy)
        {
            if (proxy.Connection?.IsServer ?? false)
            {
                throw new InvalidOperationException("cannot marshal a proxy bound to a server connection");
            }

            if (OldEncoding)
            {
                if (proxy.Protocol == Protocol.Ice1)
                {
                    Debug.Assert(proxy.Identity.Name.Length > 0);
                    proxy.Identity.IceEncode(this);
                }
                else
                {
                    Identity identity;
                    try
                    {
                        identity = Identity.FromPath(proxy.Path);
                    }
                    catch (FormatException ex)
                    {
                        throw new InvalidOperationException(
                            $"cannot marshal proxy with path '{proxy.Path}' using encoding 1.1",
                            ex);
                    }
                    if (identity.Name.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"cannot marshal proxy with path '{proxy.Path}' using encoding 1.1");
                    }

                    identity.IceEncode(this);
                }

                var proxyData = new ProxyData11(
                    proxy.FacetPath,
                    proxy.Protocol == Protocol.Ice1 && (proxy.Endpoint?.IsDatagram ?? false) ?
                        InvocationMode.Datagram : InvocationMode.Twoway,
                    secure: false,
                    proxy.Protocol,
                    protocolMinor: 0,
                    proxy.Encoding);
                proxyData.IceEncode(this);

                if (proxy.IsIndirect)
                {
                    EncodeSize(0); // 0 endpoints
                    EncodeString(proxy.IsWellKnown ? "" : proxy.Endpoint!.Host); // adapter ID unless well-known
                }
                else if (proxy.Endpoint == null)
                {
                    EncodeSize(0); // 0 endpoints
                    EncodeString(""); // empty adapter ID
                }
                else
                {
                    IEnumerable<Endpoint> endpoints = proxy.Endpoint.Transport == Transport.Coloc ?
                        proxy.AltEndpoints :
                            Enumerable.Empty<Endpoint>().Append(proxy.Endpoint).Concat(proxy.AltEndpoints);

                    if (endpoints.Any())
                    {
                        EncodeSequence(endpoints, (encoder, endpoint) => encoder.EncodeEndpoint11(endpoint));
                    }
                    else // marshaled as an endpointless proxy
                    {
                        EncodeSize(0); // 0 endpoints
                        EncodeString(""); // empty adapter ID
                    }
                }
            }
            else
            {
                string path = proxy.Path;

                // Facet is the only ice1-specific option that is encoded when using the 2.0 encoding.
                if (proxy.Facet.Length > 0)
                {
                    path = $"{path}#{Uri.EscapeDataString(proxy.Facet)}";
                }

                var proxyData = new ProxyData20(
                    path,
                    protocol: proxy.Protocol != Protocol.Ice2 ? proxy.Protocol : null,
                    encoding: proxy.Encoding != Encoding.V20 ? proxy.Encoding : null,
                    endpoint: proxy.Endpoint is Endpoint endpoint && endpoint.Transport != Transport.Coloc ?
                        endpoint.Data : null,
                    altEndpoints: proxy.AltEndpoints.Count == 0 ?
                        null : proxy.AltEndpoints.Select(e => e.Data).ToArray());

                proxyData.IceEncode(this);
            }
        }

        /// <summary>Encodes a sequence of fixed-size numeric values, such as int and long, to the buffer.</summary>
        /// <param name="v">The sequence of numeric values represented by a ReadOnlySpan.</param>
        // This method works because (as long as) there is no padding in the memory representation of the
        // ReadOnlySpan.
        public void EncodeSequence<T>(ReadOnlySpan<T> v) where T : struct
        {
            EncodeSize(v.Length);
            if (!v.IsEmpty)
            {
                WriteByteSpan(MemoryMarshal.AsBytes(v));
            }
        }

        /// <summary>Encodes a sequence of fixed-size numeric values, such as int and long, to the buffer.</summary>
        /// <param name="v">The sequence of numeric values.</param>
        public void EncodeSequence<T>(IEnumerable<T> v) where T : struct
        {
            if (v is T[] vArray)
            {
                EncodeArray(vArray);
            }
            else if (v is ImmutableArray<T> vImmutableArray)
            {
                EncodeSequence(vImmutableArray.AsSpan());
            }
            else
            {
                EncodeSequence(v, (encoder, element) => encoder.EncodeFixedSizeNumeric(element));
            }
        }

        /// <summary>Encodes a sequence to the buffer.</summary>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="encodeAction">The encode action for an element.</param>
        public void EncodeSequence<T>(IEnumerable<T> v, EncodeAction<T> encodeAction)
        {
            EncodeSize(v.Count()); // potentially slow Linq Count()
            foreach (T item in v)
            {
                encodeAction(this, item);
            }
        }

        /// <summary>Encodes a sequence to the buffer. The elements of the sequence are reference types.</summary>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="withBitSequence">True to encode null elements using a bit sequence; otherwise, false.</param>
        /// <param name="encodeAction">The encode action for a non-null element.</param>
        public void EncodeSequence<T>(IEnumerable<T?> v, bool withBitSequence, EncodeAction<T> encodeAction)
            where T : class
        {
            if (withBitSequence)
            {
                int count = v.Count(); // potentially slow Linq Count()
                EncodeSize(count);
                BitSequence bitSequence = EncodeBitSequence(count);
                int index = 0;
                foreach (T? item in v)
                {
                    if (item is T value)
                    {
                        encodeAction(this, value);
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
                EncodeSequence((IEnumerable<T>)v, encodeAction);
            }
        }

        /// <summary>Encodes a sequence of nullable values to the buffer.</summary>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="encodeAction">The encode action for the non-null values.</param>
        public void EncodeSequence<T>(IEnumerable<T?> v, EncodeAction<T> encodeAction) where T : struct
        {
            int count = v.Count(); // potentially slow Linq Count()
            EncodeSize(count);
            BitSequence bitSequence = EncodeBitSequence(count);
            int index = 0;
            foreach (T? item in v)
            {
                if (item is T value)
                {
                    encodeAction(this, value);
                }
                else
                {
                    bitSequence[index] = false;
                }
                index++;
            }
        }

        /// <summary>Encodes a mapped Slice struct to the buffer.</summary>
        /// <param name="v">The struct instance to encode.</param>
        public void EncodeStruct<T>(T v) where T : struct, IEncodable => v.IceEncode(this);

        // Encode methods for tagged basic types

        /// <summary>Encodes a tagged boolean to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The boolean to encode to the buffer.</param>
        public void EncodeTaggedBool(int tag, bool? v)
        {
            if (v is bool value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F1);
                EncodeBool(value);
            }
        }

        /// <summary>Encodes a tagged byte to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The byte to encode to the buffer.</param>
        public void EncodeTaggedByte(int tag, byte? v)
        {
            if (v is byte value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F1);
                EncodeByte(value);
            }
        }

        /// <summary>Encodes a tagged double to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The double to encode to the buffer.</param>
        public void EncodeTaggedDouble(int tag, double? v)
        {
            if (v is double value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F8);
                EncodeDouble(value);
            }
        }

        /// <summary>Encodes a tagged float to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The float to encode to the buffer.</param>
        public void EncodeTaggedFloat(int tag, float? v)
        {
            if (v is float value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F4);
                EncodeFloat(value);
            }
        }

        /// <summary>Encodes a tagged int to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The int to encode to the buffer.</param>
        public void EncodeTaggedInt(int tag, int? v)
        {
            if (v is int value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F4);
                EncodeInt(value);
            }
        }

        /// <summary>Encodes a tagged long to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The long to encode to the buffer.</param>
        public void EncodeTaggedLong(int tag, long? v)
        {
            if (v is long value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F8);
                EncodeLong(value);
            }
        }

        /// <summary>Encodes a tagged size to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The size.</param>
        public void EncodeTaggedSize(int tag, int? v)
        {
            if (v is int value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.Size);
                EncodeSize(value);
            }
        }

        /// <summary>Encodes a tagged short to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The short to encode to the buffer.</param>
        public void EncodeTaggedShort(int tag, short? v)
        {
            if (v is short value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F2);
                EncodeShort(value);
            }
        }

        /// <summary>Encodes a tagged string to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The string to encode to the buffer.</param>
        public void EncodeTaggedString(int tag, string? v)
        {
            if (v is string value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize);
                EncodeString(value);
            }
        }

        /// <summary>Encodes a tagged uint to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The uint to encode to the buffer.</param>
        public void EncodeTaggedUInt(int tag, uint? v)
        {
            if (v is uint value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F4);
                EncodeUInt(value);
            }
        }

        /// <summary>Encodes a tagged ulong to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The ulong to encode to the buffer.</param>
        public void EncodeTaggedULong(int tag, ulong? v)
        {
            if (v is ulong value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F8);
                EncodeULong(value);
            }
        }

        /// <summary>Encodes a tagged ushort to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The ushort to encode to the buffer.</param>
        public void EncodeTaggedUShort(int tag, ushort? v)
        {
            if (v is ushort value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F2);
                EncodeUShort(value);
            }
        }

        /// <summary>Encodes a tagged int to the buffer, using IceRPC's variable-size integer encoding.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The int to encode to the buffer.</param>
        public void EncodeTaggedVarInt(int tag, int? v) => EncodeTaggedVarLong(tag, v);

        /// <summary>Encodes a tagged long to the buffer, using IceRPC's variable-size integer encoding.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The long to encode to the buffer.</param>
        public void EncodeTaggedVarLong(int tag, long? v)
        {
            if (v is long value)
            {
                var format = (EncodingDefinitions.TagFormat)GetVarLongEncodedSizeExponent(value);
                EncodeTaggedParamHeader(tag, format);
                EncodeVarLong(value);
            }
        }

        /// <summary>Encodes a tagged uint to the buffer, using IceRPC's variable-size integer encoding.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The uint to encode to the buffer.</param>
        public void EncodeTaggedVarUInt(int tag, uint? v) => EncodeTaggedVarULong(tag, v);

        /// <summary>Encodes a tagged ulong to the buffer, using IceRPC's variable-size integer encoding.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The ulong to encode to the buffer.</param>
        public void EncodeTaggedVarULong(int tag, ulong? v)
        {
            if (v is ulong value)
            {
                var format = (EncodingDefinitions.TagFormat)GetVarULongEncodedSizeExponent(value);
                EncodeTaggedParamHeader(tag, format);
                EncodeVarULong(value);
            }
        }

        // Encode methods for tagged constructed types except class

        /// <summary>Encodes a tagged dictionary with fixed-size entries to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="entrySize">The size of each entry (key + value), in bytes.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the values.</param>
        public void EncodeTaggedDictionary<TKey, TValue>(
            int tag,
            IEnumerable<KeyValuePair<TKey, TValue>>? v,
            int entrySize,
            EncodeAction<TKey> keyEncodeAction,
            EncodeAction<TValue> valueEncodeAction)
            where TKey : notnull
        {
            Debug.Assert(entrySize > 1);
            if (v is IEnumerable<KeyValuePair<TKey, TValue>> dict)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize);
                int count = dict.Count();
                EncodeSize(count == 0 ? 1 : (count * entrySize) + GetSizeLength(count));
                EncodeDictionary(dict, keyEncodeAction, valueEncodeAction);
            }
        }

        /// <summary>Encodes a tagged dictionary with variable-size elements to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the values.</param>
        public void EncodeTaggedDictionary<TKey, TValue>(
            int tag,
            IEnumerable<KeyValuePair<TKey, TValue>>? v,
            EncodeAction<TKey> keyEncodeAction,
            EncodeAction<TValue> valueEncodeAction)
            where TKey : notnull
        {
            if (v is IEnumerable<KeyValuePair<TKey, TValue>> dict)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                EncodeDictionary(dict, keyEncodeAction, valueEncodeAction);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged dictionary to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="withBitSequence">When true, encodes entries with a null value using a bit sequence; otherwise,
        /// false.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the values.</param>
        public void EncodeTaggedDictionary<TKey, TValue>(
            int tag,
            IEnumerable<KeyValuePair<TKey, TValue?>>? v,
            bool withBitSequence,
            EncodeAction<TKey> keyEncodeAction,
            EncodeAction<TValue> valueEncodeAction)
            where TKey : notnull
            where TValue : class
        {
            if (v is IEnumerable<KeyValuePair<TKey, TValue?>> dict)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                EncodeDictionary(dict, withBitSequence, keyEncodeAction, valueEncodeAction);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged dictionary to the buffer. The dictionary's value type is a nullable value type.
        /// </summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the non-null values.</param>
        public void EncodeTaggedDictionary<TKey, TValue>(
            int tag,
            IEnumerable<KeyValuePair<TKey, TValue?>>? v,
            EncodeAction<TKey> keyEncodeAction,
            EncodeAction<TValue> valueEncodeAction)
            where TKey : notnull
            where TValue : struct
        {
            if (v is IEnumerable<KeyValuePair<TKey, TValue?>> dict)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                EncodeDictionary(dict, keyEncodeAction, valueEncodeAction);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged proxy.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="proxy">The proxy to encode.</param>
        public void EncodeTaggedProxy(int tag, Proxy? proxy)
        {
            if (proxy != null)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                EncodeProxy(proxy);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged sequence of fixed-size numeric values to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to encode.</param>
        public void EncodeTaggedSequence<T>(int tag, ReadOnlySpan<T> v) where T : struct
        {
            // A null T[]? or List<T>? is implicitly converted into a default aka null ReadOnlyMemory<T> or
            // ReadOnlySpan<T>. Furthermore, the span of a default ReadOnlyMemory<T> is a default ReadOnlySpan<T>, which
            // is distinct from the span of an empty sequence. This is why the "v != null" below works correctly.
            if (v != null)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize);
                int elementSize = Unsafe.SizeOf<T>();
                if (elementSize > 1)
                {
                    // This size is redundant and optimized out by the encoding when elementSize is 1.
                    EncodeSize(v.IsEmpty ? 1 : (v.Length * elementSize) + GetSizeLength(v.Length));
                }
                EncodeSequence(v);
            }
        }

        /// <summary>Encodes a tagged sequence of fixed-size numeric values to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to encode.</param>
        public void EncodeTaggedSequence<T>(int tag, IEnumerable<T>? v) where T : struct
        {
            if (v is IEnumerable<T> value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize);

                int elementSize = Unsafe.SizeOf<T>();
                if (elementSize > 1)
                {
                    int count = value.Count(); // potentially slow Linq Count()

                    // First write the size in bytes, so that the decoder can skip it. We optimize-out this byte size
                    // when elementSize is 1.
                    EncodeSize(count == 0 ? 1 : (count * elementSize) + GetSizeLength(count));
                }
                EncodeSequence(value);
            }
        }

        /// <summary>Encodes a tagged sequence of variable-size elements to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="encodeAction">The encode action for an element.</param>
        public void EncodeTaggedSequence<T>(int tag, IEnumerable<T>? v, EncodeAction<T> encodeAction)
        {
            if (v is IEnumerable<T> value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                EncodeSequence(value, encodeAction);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged sequence of fixed-size values to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="elementSize">The fixed size of each element of the sequence, in bytes.</param>
        /// <param name="encodeAction">The encode action for an element.</param>
        public void EncodeTaggedSequence<T>(int tag, IEnumerable<T>? v, int elementSize, EncodeAction<T> encodeAction)
            where T : struct
        {
            Debug.Assert(elementSize > 0);
            if (v is IEnumerable<T> value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize);

                int count = value.Count(); // potentially slow Linq Count()

                if (elementSize > 1)
                {
                    // First write the size in bytes, so that the decoder can skip it. We optimize-out this byte size
                    // when elementSize is 1.
                    EncodeSize(count == 0 ? 1 : (count * elementSize) + GetSizeLength(count));
                }
                EncodeSize(count);
                foreach (T item in value)
                {
                    encodeAction(this, item);
                }
            }
        }

        /// <summary>Encodes a tagged sequence of nullable elements to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="withBitSequence">True to encode null elements using a bit sequence; otherwise, false.</param>
        /// <param name="encodeAction">The encode action for a non-null element.</param>
        public void EncodeTaggedSequence<T>(
            int tag,
            IEnumerable<T?>? v,
            bool withBitSequence,
            EncodeAction<T> encodeAction)
            where T : class
        {
            if (v is IEnumerable<T?> value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                EncodeSequence(value, withBitSequence, encodeAction);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged sequence of nullable values to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="encodeAction">The encode action for a non-null element.</param>
        public void EncodeTaggeSequence<T>(int tag, IEnumerable<T?>? v, EncodeAction<T> encodeAction)
            where T : struct
        {
            if (v is IEnumerable<T?> value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                EncodeSequence(value, encodeAction);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged fixed-size struct to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The struct to encode.</param>
        /// <param name="fixedSize">The size of the struct, in bytes.</param>
        public void EncodeTaggedStruct<T>(int tag, T? v, int fixedSize) where T : struct, IEncodable
        {
            if (v is T value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize);
                EncodeSize(fixedSize);
                value.IceEncode(this);
            }
        }

        /// <summary>Encodes a tagged variable-size struct to the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The struct to encode.</param>
        public void EncodeTaggedStruct<T>(int tag, T? v) where T : struct, IEncodable
        {
            if (v is T value)
            {
                EncodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize);
                Position pos = StartFixedLengthSize();
                value.IceEncode(this);
                EndFixedLengthSize(pos);
            }
        }

        // Other methods

        /// <summary>Encodes a sequence of bits to the buffer, and returns this sequence backed by the buffer.</summary>
        /// <param name="bitSize">The minimum number of bits in the sequence.</param>
        /// <returns>The bit sequence, with all bits set. The actual size of the sequence is a multiple of 8.
        /// </returns>
        public BitSequence EncodeBitSequence(int bitSize)
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

        /// <summary>Computes the minimum number of bytes needed to encode a variable-length size with the 2.0 encoding.
        /// </summary>
        /// <remarks>The parameter is a long and not a varulong because sizes and size-like values are usually passed
        /// around as signed integers, even though sizes cannot be negative and are encoded like varulong values.
        /// </remarks>
        internal static int GetSizeLength20(long size)
        {
            Debug.Assert(size >= 0);
            return 1 << GetVarULongEncodedSizeExponent((ulong)size);
        }

        // Constructs a Ice encoder
        internal IceEncoder(Encoding encoding, Memory<byte> initialBuffer = default, FormatType classFormat = default)
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

        /// <summary>Computes the amount of data encoded from the start position to the current position and writes that
        /// size at the start position (as a fixed-length size). The size does not include its own encoded length.
        /// </summary>
        /// <param name="start">The start position.</param>
        /// <param name="sizeLength">The number of bytes used to marshal the size 1, 2 or 4.</param>
        internal void EndFixedLengthSize(Position start, int sizeLength = DefaultSizeLength)
        {
            Debug.Assert(start.Offset >= 0);
            if (OldEncoding)
            {
                EncodeFixedLengthSize11(Distance(start) - 4, start);
            }
            else
            {
                EncodeFixedLengthSize20(Distance(start) - sizeLength, start, sizeLength);
            }
        }

        /// <summary>Finishes off the underlying buffer vector and returns it. You should not write additional data to
        /// this Ice encoder after calling Finish, however rewriting previous data (with for example
        /// <see cref="EndFixedLengthSize"/>) is fine.</summary>
        /// <returns>The buffers.</returns>
        internal ReadOnlyMemory<ReadOnlyMemory<byte>> Finish()
        {
            _bufferVector.Span[^1] = _bufferVector.Span[^1].Slice(0, _tail.Offset);
            return _bufferVector;
        }

        /// <summary>Encodes a size on a fixed number of bytes at the given position.</summary>
        /// <param name="size">The size to encode.</param>
        /// <param name="pos">The position to encode to.</param>
        /// <param name="sizeLength">The number of bytes used to encode the size. Can be 1, 2 or 4.</param>
        internal void EncodeFixedLengthSize20(int size, Position pos, int sizeLength = DefaultSizeLength)
        {
            Debug.Assert(pos.Buffer < _bufferVector.Length);
            Debug.Assert(sizeLength == 1 || sizeLength == 2 || sizeLength == 4);

            Span<byte> data = stackalloc byte[sizeLength];
            data.EncodeFixedLengthSize20(size);
            RewriteByteSpan(data, pos);
        }

        /// <summary>Returns the current position and writes placeholder for a fixed-length size value. The
        /// position must be used to rewrite the size later.</summary>
        /// <param name="sizeLength">The number of bytes reserved to encode the fixed-length size.</param>
        /// <returns>The position before writing the size.</returns>
        internal Position StartFixedLengthSize(int sizeLength = DefaultSizeLength)
        {
            Position pos = _tail;
            WriteByteSpan(stackalloc byte[OldEncoding ? 4 : sizeLength]); // placeholder for future size
            return pos;
        }

        /// <summary>Writes a span of bytes. The encoder capacity is expanded if required, the size and tail position are
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

        internal void EncodeEndpoint11(Endpoint endpoint)
        {
            Debug.Assert(OldEncoding);

            this.Encode(endpoint.Transport);
            Position startPos = _tail;

            EncodeInt(0); // placeholder for future encapsulation size
            if (endpoint is OpaqueEndpoint opaqueEndpoint)
            {
                opaqueEndpoint.ValueEncoding.IceEncode(this);
                WriteByteSpan(opaqueEndpoint.Value.Span); // WriteByteSpan is not encoding-sensitive
            }
            else
            {
                Encoding.IceEncode(this);
                if (endpoint.Protocol == Protocol.Ice1)
                {
                    endpoint.EncodeOptions11(this);
                }
                else
                {
                    EncodeString(endpoint.Data.Host);
                    EncodeUShort(endpoint.Data.Port);
                    EncodeSequence(endpoint.Data.Options, BasicEncodeActions.StringEncodeAction);
                }
            }
            EncodeFixedLengthSize11(Distance(startPos), startPos);
        }

        internal void EncodeField(int key, ReadOnlySpan<byte> value)
        {
            EncodeVarInt(key);
            EncodeSize(value.Length);
            WriteByteSpan(value);
        }

        internal void EncodeField<T>(int key, T value, EncodeAction<T> encodeAction)
        {
            EncodeVarInt(key);
            Position pos = StartFixedLengthSize(2); // 2-bytes size place holder
            encodeAction(this, value);
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

        /// <summary>Expands the encoder's buffer to make room for more data. If the bytes remaining in the buffer are
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
                    // First Expand for a new Ice encoder constructed with no buffer.
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

        /// <summary>Computes the minimum number of bytes needed to encode a variable-length size with the current
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

        /// <summary>Encodes a size on 4 bytes at the given position.</summary>
        /// <param name="size">The size to encode.</param>
        /// <param name="pos">The position to encode to.</param>
        internal void EncodeFixedLengthSize11(int size, Position pos)
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

        /// <summary>Encodes a fixed-size numeric value to the buffer.</summary>
        /// <param name="v">The numeric value to encode to the buffer.</param>
        private void EncodeFixedSizeNumeric<T>(T v) where T : struct
        {
            int elementSize = Unsafe.SizeOf<T>();
            Span<byte> data = stackalloc byte[elementSize];
            MemoryMarshal.Write(data, ref v);
            WriteByteSpan(data);
        }

        /// <summary>Encodes the header for a tagged parameter or data member.</summary>
        /// <param name="tag">The numeric tag associated with the parameter or data member.</param>
        /// <param name="format">The tag format.</param>
        private void EncodeTaggedParamHeader(int tag, EncodingDefinitions.TagFormat format)
        {
            Debug.Assert(format != EncodingDefinitions.TagFormat.VInt); // VInt cannot be marshaled

            int v = (int)format;
            if (tag < 30)
            {
                v |= tag << 3;
                EncodeByte((byte)v);
            }
            else
            {
                v |= 0x0F0; // tag = 30
                EncodeByte((byte)v);
                EncodeSize(tag);
            }
            if (_current.InstanceType != InstanceType.None)
            {
                _current.SliceFlags |= EncodingDefinitions.SliceFlags.HasTaggedMembers;
            }
        }
    }
}
