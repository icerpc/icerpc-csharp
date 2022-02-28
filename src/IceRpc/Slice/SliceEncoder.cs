// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using static IceRpc.Slice.Internal.Slice11Definitions;

namespace IceRpc.Slice
{
    /// <summary>Encodes data into one or more byte buffers using the Slice encoding.</summary>
    public ref partial struct SliceEncoder
    {
        /// <summary>The number of bytes encoded by this encoder into the underlying buffer writer.</summary>
        public int EncodedByteCount { get; private set; }

        /// <summary>The Slice encoding associated with this encoder.</summary>
        public SliceEncoding Encoding { get; }

        internal const long VarLongMinValue = -2_305_843_009_213_693_952; // -2^61
        internal const long VarLongMaxValue = 2_305_843_009_213_693_951; // 2^61 - 1
        internal const ulong VarULongMinValue = 0;
        internal const ulong VarULongMaxValue = 4_611_686_018_427_387_903; // 2^62 - 1

        private static readonly UTF8Encoding _utf8 =
            new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true); // no BOM

        private readonly IBufferWriter<byte> _bufferWriter;

        private ClassContext _classContext;

        private Encoder? _utf8Encoder; // initialized lazily

        /// <summary>Constructs an Slice encoder.</summary>
        /// <param name="bufferWriter">The buffer writer that provides the buffers to write into.</param>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="classFormat">The class format (1.1 only).</param>
        public SliceEncoder(IBufferWriter<byte> bufferWriter, SliceEncoding encoding, FormatType classFormat = default)
            : this()
        {
            Encoding = encoding;
            _bufferWriter = bufferWriter;
            _classContext = new ClassContext(classFormat);
        }

        // Encode methods for basic types

        /// <summary>Encodes a boolean.</summary>
        /// <param name="v">The boolean to encode.</param>
        public void EncodeBool(bool v) => EncodeByte(v ? (byte)1 : (byte)0);

        /// <summary>Encodes a byte.</summary>
        /// <param name="v">The byte to encode.</param>
        public void EncodeByte(byte v)
        {
            Span<byte> span = _bufferWriter.GetSpan();
            span[0] = v;
            Advance(1);
        }

        /// <summary>Encodes a double.</summary>
        /// <param name="v">The double to encode.</param>
        public void EncodeDouble(double v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a float.</summary>
        /// <param name="v">The float to encode.</param>
        public void EncodeFloat(float v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes an int.</summary>
        /// <param name="v">The int to encode.</param>
        public void EncodeInt(int v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a long.</summary>
        /// <param name="v">The long to encode.</param>
        public void EncodeLong(long v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a short.</summary>
        /// <param name="v">The short to encode.</param>
        public void EncodeShort(short v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a size on variable number of bytes.</summary>
        /// <param name="v">The size to encode.</param>
        public void EncodeSize(int v)
        {
            if (Encoding == IceRpc.Encoding.Slice11)
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

        /// <summary>Encodes a string.</summary>
        /// <param name="v">The string to encode.</param>
        public void EncodeString(string v)
        {
            if (v.Length == 0)
            {
                EncodeSize(0);
            }
            else
            {
                int maxSize = _utf8.GetMaxByteCount(v.Length);
                int sizeLength = GetSizeLength(maxSize);
                Span<byte> sizePlaceholder = GetPlaceholderSpan(sizeLength);

                Span<byte> currentSpan = _bufferWriter.GetSpan();
                if (currentSpan.Length >= maxSize)
                {
                    // Encode directly into currentSpan
                    int size = _utf8.GetBytes(v, currentSpan);
                    Encoding.EncodeSize(size, sizePlaceholder);
                    Advance(size);
                }
                else
                {
                    // Encode piecemeal using _utf8Encoder
                    if (_utf8Encoder == null)
                    {
                        _utf8Encoder = _utf8.GetEncoder();
                    }
                    else
                    {
                        _utf8Encoder.Reset();
                    }

                    ReadOnlySpan<char> chars = v.AsSpan();
                    _utf8Encoder.Convert(chars, _bufferWriter, flush: true, out long bytesUsed, out bool completed);

                    Debug.Assert(completed); // completed is always true when flush is true
                    int size = checked((int)bytesUsed);
                    EncodedByteCount += size;
                    Encoding.EncodeSize(size, sizePlaceholder);
                }
            }
        }

        /// <summary>Encodes a uint.</summary>
        /// <param name="v">The uint to encode.</param>
        public void EncodeUInt(uint v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a ulong.</summary>
        /// <param name="v">The ulong to encode.</param>
        public void EncodeULong(ulong v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a ushort.</summary>
        /// <param name="v">The ushort to encode.</param>
        public void EncodeUShort(ushort v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes an int using IceRPC's variable-size integer encoding.</summary>
        /// <param name="v">The int to encode.</param>
        public void EncodeVarInt(int v) => EncodeVarLong(v);

        /// <summary>Encodes a long using IceRPC's variable-size integer encoding, with the minimum number of bytes
        /// required by the encoding.</summary>
        /// <param name="v">The long to encode. It must be in the range [-2^61..2^61 - 1].</param>
        public void EncodeVarLong(long v)
        {
            int encodedSizeExponent = GetVarLongEncodedSizeExponent(v);
            v <<= 2;
            v |= (uint)encodedSizeExponent;

            Span<byte> data = _bufferWriter.GetSpan(sizeof(long));
            MemoryMarshal.Write(data, ref v);
            Advance(1 << encodedSizeExponent);
        }

        /// <summary>Encodes a uint using IceRPC's variable-size integer encoding.</summary>
        /// <param name="v">The uint to encode.</param>
        public void EncodeVarUInt(uint v) => EncodeVarULong(v);

        /// <summary>Encodes a ulong using IceRPC's variable-size integer encoding, with the minimum
        /// number of bytes required by the encoding.</summary>
        /// <param name="v">The ulong to encode. It must be in the range [0..2^62 - 1].</param>
        public void EncodeVarULong(ulong v)
        {
            int encodedSizeExponent = GetVarULongEncodedSizeExponent(v);
            v <<= 2;
            v |= (uint)encodedSizeExponent;

            Span<byte> data = _bufferWriter.GetSpan(sizeof(ulong));
            MemoryMarshal.Write(data, ref v);
            Advance(1 << encodedSizeExponent);
        }

        // Encode methods for constructed types

        /// <summary>Encodes a remote exception.</summary>
        /// <param name="v">The remote exception to encode.</param>
        public void EncodeException(RemoteException v)
        {
            if (Encoding == IceRpc.Encoding.Slice11)
            {
                EncodeExceptionClass(v);
            }
            else
            {
                v.Encode(ref this);
            }
        }

        /// <summary>Encodes a nullable proxy.</summary>
        /// <param name="bitSequenceWriter">The bit sequence writer.</param>
        /// <param name="proxy">The proxy to encode, or null.</param>
        public void EncodeNullableProxy(ref BitSequenceWriter bitSequenceWriter, Proxy? proxy)
        {
            if (Encoding == IceRpc.Encoding.Slice11)
            {
                if (proxy != null)
                {
                    EncodeProxy(proxy);
                }
                else
                {
                    Identity.Empty.Encode(ref this);
                }
            }
            else
            {
                bitSequenceWriter.Write(proxy != null);
                if (proxy != null)
                {
                    EncodeProxy(proxy);
                }
            }
        }

        /// <summary>Encodes a non-null proxy.</summary>
        /// <param name="proxy">The proxy to encode.</param>
        public void EncodeProxy(Proxy proxy)
        {
            if (proxy.Connection?.IsServer ?? false)
            {
                throw new InvalidOperationException("cannot encode a proxy bound to a server connection");
            }

            if (Encoding == IceRpc.Encoding.Slice11)
            {
                this.EncodeIdentityPath(proxy.Path);
                (byte encodingMajor, byte encodingMinor) = proxy.Encoding.ToMajorMinor();

                var proxyData = new ProxyData(
                    proxy.Fragment,
                    GetInvocationMode(proxy),
                    secure: false,
                    protocolMajor: proxy.Protocol.ToByte(),
                    protocolMinor: 0,
                    encodingMajor,
                    encodingMinor);
                proxyData.Encode(ref this);

                if (proxy.Endpoint is Endpoint endpoint)
                {
                    EncodeSize(1 + proxy.AltEndpoints.Count); // endpoint count
                    EncodeEndpoint(endpoint);
                    foreach (Endpoint altEndpoint in proxy.AltEndpoints)
                    {
                        EncodeEndpoint(altEndpoint);
                    }
                }
                else
                {
                    EncodeSize(0); // 0 endpoints
                    int maxCount = proxy.Params.TryGetValue("adapter-id", out string? adapterId) ? 1 : 0;

                    if (proxy.Params.Count > maxCount)
                    {
                        throw new NotSupportedException(
                            "cannot encode proxy with parameter other than adapter-id using Slice 1.1");
                    }
                    EncodeString(adapterId ?? "");
                }
            }
            else
            {
                EncodeString(proxy.ToString()); // a URI or an absolute path
            }

            static InvocationMode GetInvocationMode(Proxy proxy) =>
                proxy.Protocol == Protocol.Ice &&
                proxy.Endpoint is Endpoint endpoint &&
                endpoint.Params.TryGetValue("transport", out string? transport) &&
                transport == TransportNames.Udp ? InvocationMode.Oneway : InvocationMode.Twoway;
        }

        // Other methods

        /// <summary>Computes the minimum number of bytes required to encode a long value using the Slice encoding
        /// variable-size encoded representation.</summary>
        /// <param name="value">The long value.</param>
        /// <returns>The minimum number of bytes required to encode <paramref name="value"/>. Can be 1, 2, 4 or 8.
        /// </returns>
        public static int GetVarLongEncodedSize(long value) => 1 << GetVarLongEncodedSizeExponent(value);

        /// <summary>Computes the minimum number of bytes required to encode a ulong value using the Slice encoding
        /// variable-size encoded representation.</summary>
        /// <param name="value">The ulong value.</param>
        /// <returns>The minimum number of bytes required to encode <paramref name="value"/>. Can be 1, 2, 4 or 8.
        /// </returns>
        public static int GetVarULongEncodedSize(ulong value) => 1 << GetVarULongEncodedSizeExponent(value);

        /// <summary>Encodes a var ulong into a span of bytes using a fixed number of bytes.</summary>
        /// <param name="value">The value to encode.</param>
        /// <param name="into">The destination byte buffer, which must be 1, 2, 4 or 8 bytes long.</param>
        public static void EncodeVarULong(ulong value, Span<byte> into)
        {
            int sizeLength = into.Length;
            Debug.Assert(sizeLength == 1 || sizeLength == 2 || sizeLength == 4 || sizeLength == 8);

            (uint encodedSizeExponent, long maxSize) = sizeLength switch
            {
                1 => (0x00u, 63), // 2^6 - 1
                2 => (0x01u, 16_383), // 2^14 - 1
                4 => (0x02u, 1_073_741_823), // 2^30 - 1
                _ => (0x03u, (long)VarULongMaxValue)
            };

            if (value > (ulong)maxSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"'{value}' cannot be encoded on {sizeLength} bytes");
            }

            Span<byte> ulongBuf = stackalloc byte[8];
            value <<= 2;

            value |= encodedSizeExponent;
            MemoryMarshal.Write(ulongBuf, ref value);
            ulongBuf[0..sizeLength].CopyTo(into);
        }

        /// <summary>Encodes a non-null tagged value. The number of bytes needed to encode the value is not known before
        /// encoding this value.</summary>
        /// <param name="tag">The tag. Must be either FSize or OVSize.</param>
        /// <param name="tagFormat">The tag format.</param>
        /// <param name="v">The value to encode.</param>
        /// <param name="encodeAction">The delegate that encodes the value after the tag header.</param>
        public void EncodeTagged<T>(
            int tag,
            TagFormat tagFormat,
            T v,
            EncodeAction<T> encodeAction) where T : notnull
        {
            if (Encoding == IceRpc.Encoding.Slice11)
            {
                if (tagFormat == TagFormat.FSize)
                {
                    EncodeTaggedParamHeader(tag, tagFormat);
                    Span<byte> placeholder = GetPlaceholderSpan(4);
                    int startPos = EncodedByteCount;
                    encodeAction(ref this, v);

                    // We don't include the size-length in the size we encode.
                    EncodeInt(EncodedByteCount - startPos, placeholder);
                }
                else
                {
                    // A VSize where the size is optimized out. Used here for strings (and only strings) because we cannot
                    // easily compute the number of UTF-8 bytes in a C# string before encoding it.
                    Debug.Assert(tagFormat == TagFormat.OVSize);

                    EncodeTaggedParamHeader(tag, TagFormat.VSize);
                    encodeAction(ref this, v);
                }
            }
            else
            {
                EncodeVarInt(tag); // the key
                Span<byte> sizePlaceholder = GetPlaceholderSpan(4);
                int startPos = EncodedByteCount;
                encodeAction(ref this, v);
                Slice20Encoding.EncodeSize(EncodedByteCount - startPos, sizePlaceholder);
            }
        }

        /// <summary>Encodes a non-null tagged value. The number of bytes needed to encode the value is known before
        /// encoding the value.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="tagFormat">The tag format. Can have any value except FSize.</param>
        /// <param name="size">The number of bytes needed to encode the value.</param>
        /// <param name="v">The value to encode.</param>
        /// <param name="encodeAction">The delegate that encodes the value after the tag header.</param>
        public void EncodeTagged<T>(
            int tag,
            TagFormat tagFormat,
            int size,
            T v,
            EncodeAction<T> encodeAction) where T : notnull
        {
            int startPos;

            if (Encoding == IceRpc.Encoding.Slice11)
            {
                Debug.Assert(tagFormat != TagFormat.FSize);
                Debug.Assert(size > 0);

                bool encodeSize = tagFormat == TagFormat.VSize;

                tagFormat = tagFormat switch
                {
                    TagFormat.VInt => size switch
                    {
                        1 => TagFormat.F1,
                        2 => TagFormat.F2,
                        4 => TagFormat.F4,
                        8 => TagFormat.F8,
                        _ => throw new ArgumentException($"invalid value for size: {size}", nameof(size))
                    },

                    TagFormat.OVSize => TagFormat.VSize, // size encoding is optimized out

                    _ => tagFormat
                };

                EncodeTaggedParamHeader(tag, tagFormat);

                if (encodeSize)
                {
                    EncodeSize(size);
                }

                startPos = EncodedByteCount;
                encodeAction(ref this, v);
            }
            else
            {
                EncodeVarInt(tag); // the key
                EncodeSize(size);
                startPos = EncodedByteCount;
                encodeAction(ref this, v);
            }

            int actualSize = EncodedByteCount - startPos;
            if (actualSize != size)
            {
                throw new ArgumentException($"value of size ({size}) does not match encoded size ({actualSize})",
                                            nameof(size));
            }
        }

        /// <summary>Allocates a new bit sequence in the underlying buffer(s) and returns a writer for this bit
        /// sequence.</summary>
        /// <param name="bitSequenceSize">The minimum number of bits in the bit sequence.</param>
        /// <returns>The bit sequence writer.</returns>
        public BitSequenceWriter GetBitSequenceWriter(int bitSequenceSize)
        {
            if (Encoding == SliceEncoding.Slice11)
            {
                return default;
            }
            else
            {
                if (bitSequenceSize <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(bitSequenceSize),
                        $"{nameof(bitSequenceSize)} must be greater than 0");
                }

                int remaining = (bitSequenceSize >> 3) + ((bitSequenceSize & 0x07) != 0 ? 1 : 0); // size in bytes

                Span<byte> firstSpan = _bufferWriter.GetSpan();
                Span<byte> secondSpan = default;

                // We only create this additionalMemory list in the rare situation where 2 spans are not sufficient.
                List<Memory<byte>>? additionalMemory = null;

                if (firstSpan.Length >= remaining)
                {
                    firstSpan = firstSpan[0..remaining];
                    Advance(remaining);
                }
                else
                {
                    Advance(firstSpan.Length);
                    remaining -= firstSpan.Length;

                    secondSpan = _bufferWriter.GetSpan();
                    if (secondSpan.Length >= remaining)
                    {
                        secondSpan = secondSpan[0..remaining];
                        Advance(remaining);
                    }
                    else
                    {
                        Advance(secondSpan.Length);
                        remaining -= secondSpan.Length;
                        additionalMemory = new List<Memory<byte>>();

                        do
                        {
                            Memory<byte> memory = _bufferWriter.GetMemory();
                            if (memory.Length >= remaining)
                            {
                                additionalMemory.Add(memory[0..remaining]);
                                Advance(remaining);
                                remaining = 0;
                            }
                            else
                            {
                                additionalMemory.Add(memory);
                                Advance(memory.Length);
                                remaining -= memory.Length;
                            }
                        } while (remaining > 0);
                    }
                }

                return new BitSequenceWriter(new SpanEnumerator(firstSpan, secondSpan, additionalMemory));
            }
        }

        /// <summary>Gets a placeholder to be filled-in later.</summary>
        /// <param name="size">The size of the placeholder, typically a small number like 4.</param>
        /// <returns>A buffer of length <paramref name="size"/>.</returns>
        /// <remarks>We make the assumption the underlying buffer writer allows rewriting memory it provided even after
        /// successive calls to GetMemory/GetSpan and Advance.</remarks>
        public Span<byte> GetPlaceholderSpan(int size)
        {
            Debug.Assert(size > 0);
            Span<byte> placeholder = _bufferWriter.GetSpan(size)[0..size];
            Advance(size);
            return placeholder;
        }

        /// <summary>Computes the minimum number of bytes needed to encode a variable-length size.</summary>
        /// <param name="size">The size.</param>
        /// <returns>The minimum number of bytes.</returns>
        public int GetSizeLength(int size) => Encoding == IceRpc.Encoding.Slice11 ?
            (size < 255 ? 1 : 5) : GetVarULongEncodedSize(checked((ulong)size));

        /// <summary>Copies a span of bytes to the underlying buffer writer.</summary>
        /// <param name="span">The span to copy.</param>
        public void WriteByteSpan(ReadOnlySpan<byte> span)
        {
            _bufferWriter.Write(span);
            EncodedByteCount += span.Length;
        }

        internal static void EncodeInt(int v, Span<byte> into) => MemoryMarshal.Write(into, ref v);

        /// <summary>Encodes a fixed-size numeric value.</summary>
        /// <param name="v">The numeric value to encode.</param>
        internal void EncodeFixedSizeNumeric<T>(T v) where T : struct
        {
            int elementSize = Unsafe.SizeOf<T>();
            Span<byte> data = _bufferWriter.GetSpan(elementSize)[0..elementSize];
            MemoryMarshal.Write(data, ref v);
            Advance(elementSize);
        }

        /// <summary>Gets a placeholder to be filled-in later.</summary>
        /// <param name="size">The size of the placeholder, typically a small number like 4.</param>
        /// <returns>A buffer of length <paramref name="size"/>.</returns>
        /// <remarks>We make the assumption the underlying buffer writer allows rewriting memory it provided even after
        /// successive calls to GetMemory/GetSpan and Advance.</remarks>
        internal Memory<byte> GetPlaceholderMemory(int size)
        {
            Debug.Assert(size > 0);
            Memory<byte> placeholder = _bufferWriter.GetMemory(size)[0..size];
            Advance(size);
            return placeholder;
        }

        /// <summary>Gets the minimum number of bytes needed to encode a long value with the varlong encoding as an
        /// exponent of 2.</summary>
        /// <param name="value">The value to encode.</param>
        /// <returns>N where 2^N is the number of bytes needed to encode value with IceRPC's varlong encoding.</returns>
        private static int GetVarLongEncodedSizeExponent(long value)
        {
            if (value < VarLongMinValue || value > VarLongMaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"varlong value '{value}' is out of range");
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
            if (value > VarULongMaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"varulong value '{value}' is out of range");
            }

            return (value << 2) switch
            {
                ulong b when b <= byte.MaxValue => 0,
                ulong s when s <= ushort.MaxValue => 1,
                ulong i when i <= uint.MaxValue => 2,
                _ => 3
            };
        }

        private void Advance(int count)
        {
            _bufferWriter.Advance(count);
            EncodedByteCount += count;
        }

        /// <summary>Encodes an endpoint in a nested encapsulation (1.1 only).</summary>
        /// <param name="endpoint">The endpoint to encode.</param>
        private void EncodeEndpoint(Endpoint endpoint)
        {
            Debug.Assert(Encoding == IceRpc.Encoding.Slice11);

            // If there is no transport parameter, we default to TCP.
            if (!endpoint.Params.TryGetValue("transport", out string? transport))
            {
                transport = TransportNames.Tcp;
            }

            // The 1.1 encoding of ice endpoints is transport-specific, and hard-coded here. The preferred and
            // fallback encoding for new transports is TransportCode.Uri.

            if (endpoint.Protocol == Protocol.Ice && transport == TransportNames.Opaque)
            {
                // Opaque endpoint encoding

                (TransportCode transportCode, byte encodingMajor, byte encodingMinor, ReadOnlyMemory<byte> bytes) =
                    endpoint.ParseOpaqueParams();

                this.EncodeTransportCode(transportCode);
                EncodeInt(4 + 2 + bytes.Length); // encapsulation size includes size-length and 2 bytes for encoding
                EncodeByte(encodingMajor);
                EncodeByte(encodingMinor);
                WriteByteSpan(bytes.Span);
            }
            else
            {
                TransportCode transportCode = TransportCode.Uri;
                bool compress = false;
                int timeout = -1;

                if (endpoint.Protocol == Protocol.Ice)
                {
                    if (transport == TransportNames.Tcp)
                    {
                        (compress, timeout, bool? tls) = endpoint.ParseTcpParams();
                        transportCode = (tls ?? true) ? TransportCode.SSL : TransportCode.TCP;
                    }
                    else if (transport == TransportNames.Udp)
                    {
                        transportCode = TransportCode.UDP;
                        compress = endpoint.ParseUdpParams().Compress;
                    }
                }
                // else transportCode remains Uri

                this.EncodeTransportCode(transportCode);

                int startPos = EncodedByteCount; // size includes size-length
                Span<byte> sizePlaceholder = GetPlaceholderSpan(4); // encapsulation size
                EncodeByte(1); // encoding version major
                EncodeByte(1); // encoding version minor

                switch (transportCode)
                {
                    case TransportCode.TCP:
                    case TransportCode.SSL:
                    {
                        EncodeString(endpoint.Host);
                        EncodeInt(endpoint.Port);
                        EncodeInt(timeout);
                        EncodeBool(compress);
                        break;
                    }

                    case TransportCode.UDP:
                    {
                        EncodeString(endpoint.Host);
                        EncodeInt(endpoint.Port);
                        EncodeBool(compress);
                        break;
                    }

                    default:
                        Debug.Assert(transportCode == TransportCode.Uri);
                        EncodeString(endpoint.ToString());
                        break;
                }

                EncodeInt(EncodedByteCount - startPos, sizePlaceholder);
            }
        }

        /// <summary>Encodes the header for a tagged parameter or data member. Slice 1.1 only.</summary>
        /// <param name="tag">The numeric tag associated with the parameter or data member.</param>
        /// <param name="format">The tag format.</param>
        private void EncodeTaggedParamHeader(int tag, TagFormat format)
        {
            Debug.Assert(Encoding == IceRpc.Encoding.Slice11);
            Debug.Assert(format != TagFormat.VInt && format != TagFormat.OVSize); // VInt/OVSize cannot be encoded

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

            if (_classContext.Current.InstanceType != InstanceType.None)
            {
                _classContext.Current.SliceFlags |= SliceFlags.HasTaggedMembers;
            }
        }
    }
}
