// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using static IceRpc.Slice.Internal.Slice1Definitions;

namespace IceRpc.Slice
{
    /// <summary>Encodes data into one or more byte buffers using the Slice encoding.</summary>
    public ref partial struct SliceEncoder
    {
        /// <summary>The number of bytes encoded by this encoder into the underlying buffer writer.</summary>
        public int EncodedByteCount { get; private set; }

        /// <summary>The Slice encoding of this encoder.</summary>
        public SliceEncoding Encoding { get; }

        internal const long VarInt62MinValue = -2_305_843_009_213_693_952; // -2^61
        internal const long VarInt62MaxValue = 2_305_843_009_213_693_951; // 2^61 - 1
        internal const ulong VarUInt62MinValue = 0;
        internal const ulong VarUInt62MaxValue = 4_611_686_018_427_387_903; // 2^62 - 1

        private static readonly UTF8Encoding _utf8 =
            new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true); // no BOM

        private readonly IBufferWriter<byte> _bufferWriter;

        private ClassContext _classContext;

        private Encoder? _utf8Encoder; // initialized lazily

        /// <summary>Constructs an Slice encoder.</summary>
        /// <param name="pipeWriter">The pipe writer that provides the buffers to write into.</param>
        /// <param name="encoding">The Slice encoding.</param>
        /// <param name="classFormat">The class format (Slice1 only).</param>
        public SliceEncoder(PipeWriter pipeWriter, SliceEncoding encoding, FormatType classFormat = default)
            : this((IBufferWriter<byte>)pipeWriter, encoding, classFormat)
        {
        }

        // Encode methods for basic types

        /// <summary>Encodes a bool into a Slice bool.</summary>
        /// <param name="v">The boolean to encode.</param>
        public void EncodeBool(bool v) => EncodeUInt8(v ? (byte)1 : (byte)0);

        /// <summary>Encodes a float into a Slice float32.</summary>
        /// <param name="v">The float to encode.</param>
        public void EncodeFloat32(float v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a double into a Slice float64.</summary>
        /// <param name="v">The double to encode.</param>
        public void EncodeFloat64(double v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes an sbyte into a Slice int8.</summary>
        /// <param name="v">The sbyte to encode.</param>
        public void EncodeInt8(sbyte v) => EncodeUInt8((byte)v);

        /// <summary>Encodes a short into a Slice int16.</summary>
        /// <param name="v">The short to encode.</param>
        public void EncodeInt16(short v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes an int into a Slice int32.</summary>
        /// <param name="v">The int to encode.</param>
        public void EncodeInt32(int v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a long into a Slice int64.</summary>
        /// <param name="v">The long to encode.</param>
        public void EncodeInt64(long v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a size on variable number of bytes.</summary>
        /// <param name="v">The size to encode.</param>
        public void EncodeSize(int v)
        {
            if (Encoding == SliceEncoding.Slice1)
            {
                if (v < 255)
                {
                    EncodeUInt8((byte)v);
                }
                else
                {
                    EncodeUInt8(255);
                    EncodeInt32(v);
                }
            }
            else
            {
                EncodeVarUInt62((ulong)v);
            }
        }

        /// <summary>Encodes a string into a Slice string.</summary>
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
                    EncodeSizeIntoPlaceholder(Encoding, size, sizePlaceholder);
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
                    EncodeSizeIntoPlaceholder(Encoding, size, sizePlaceholder);
                }
            }

            static void EncodeSizeIntoPlaceholder(SliceEncoding encoding, int size, Span<byte> into)
            {
                if (encoding == SliceEncoding.Slice1)
                {
                    if (into.Length == 1)
                    {
                        Debug.Assert(size < 255);
                        into[0] = (byte)size;
                    }
                    else
                    {
                        Debug.Assert(into.Length == 5);
                        into[0] = 255;
                        EncodeInt32(size, into[1..]);
                    }
                }
                else
                {
                    EncodeVarUInt62((ulong)size, into);
                }
            }
        }

        /// <summary>Encodes a byte into a Slice uint8.</summary>
        /// <param name="v">The byte to encode.</param>
        public void EncodeUInt8(byte v)
        {
            Span<byte> span = _bufferWriter.GetSpan();
            span[0] = v;
            Advance(1);
        }

        /// <summary>Encodes a ushort into a Slice uint16.</summary>
        /// <param name="v">The ushort to encode.</param>
        public void EncodeUInt16(ushort v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a uint into a Slice uint32.</summary>
        /// <param name="v">The uint to encode.</param>
        public void EncodeUInt32(uint v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a ulong into a Slice uint64.</summary>
        /// <param name="v">The ulong to encode.</param>
        public void EncodeUInt64(ulong v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes an int into a Slice varint32.</summary>
        /// <param name="v">The int to encode.</param>
        public void EncodeVarInt32(int v) => EncodeVarInt62(v);

        /// <summary>Encodes a long into a Slice varint62, with the minimum number of bytes required
        /// by the encoding.</summary>
        /// <param name="v">The long to encode. It must be in the range [-2^61..2^61 - 1].</param>
        public void EncodeVarInt62(long v)
        {
            int encodedSizeExponent = GetVarInt62EncodedSizeExponent(v);
            v <<= 2;
            v |= (uint)encodedSizeExponent;

            Span<byte> data = _bufferWriter.GetSpan(sizeof(long));
            MemoryMarshal.Write(data, ref v);
            Advance(1 << encodedSizeExponent);
        }

        /// <summary>Encodes a uint into a Slice varuint32.</summary>
        /// <param name="v">The uint to encode.</param>
        public void EncodeVarUInt32(uint v) => EncodeVarUInt62(v);

        /// <summary>Encodes a ulong into a Slice varuint62, with the minimum number of bytes
        /// required by the encoding.</summary>
        /// <param name="v">The ulong to encode. It must be in the range [0..2^62 - 1].</param>
        public void EncodeVarUInt62(ulong v)
        {
            int encodedSizeExponent = GetVarUInt62EncodedSizeExponent(v);
            v <<= 2;
            v |= (uint)encodedSizeExponent;

            Span<byte> data = _bufferWriter.GetSpan(sizeof(ulong));
            MemoryMarshal.Write(data, ref v);
            Advance(1 << encodedSizeExponent);
        }

        // Encode methods for constructed types

        /// <summary>Encodes a nullable proxy.</summary>
        /// <param name="bitSequenceWriter">The bit sequence writer.</param>
        /// <param name="proxy">The proxy to encode, or null.</param>
        public void EncodeNullableProxy(ref BitSequenceWriter bitSequenceWriter, Proxy? proxy)
        {
            if (Encoding == SliceEncoding.Slice1)
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
            if (Encoding == SliceEncoding.Slice1)
            {
                this.EncodeIdentityPath(proxy.Path);
                const byte encodingMajor = 1;
                const byte encodingMinor = 1;

                var proxyData = new ProxyData(
                    proxy.Fragment,
                    InvocationMode.Twoway,
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
                            "cannot encode proxy with parameter other than adapter-id using Slice1");
                    }
                    EncodeString(adapterId ?? "");
                }
            }
            else
            {
                EncodeString(proxy.ToString()); // a URI or an absolute path
            }
        }

        /// <summary>Encodes a dispatch exception as a Slice1 system exception.</summary>
        /// <param name="v">The dispatch exception to encode.</param>
        /// <param name="path">The path to include in some system exceptions.</param>
        /// <param name="fragment">The fragment to include in some system exceptions.</param>
        /// <param name="operation">The operation to include in some system exceptions.</param>
        /// <remarks>A dispatch exception cannot be encoded directly with Slice1.</remarks>
        public void EncodeSystemException(DispatchException v, string path, string fragment, string operation)
        {
            Debug.Assert(Encoding == SliceEncoding.Slice1);

            DispatchErrorCode errorCode = v.ErrorCode;

            switch (errorCode)
            {
                case DispatchErrorCode.ServiceNotFound:
                case DispatchErrorCode.OperationNotFound:
                    this.EncodeReplyStatus(errorCode == DispatchErrorCode.ServiceNotFound ?
                        ReplyStatus.ObjectNotExistException : ReplyStatus.OperationNotExistException);

                    new RequestFailedExceptionData(path, fragment, operation).Encode(ref this);
                    break;

                default:
                    this.EncodeReplyStatus(ReplyStatus.UnknownException);
                    // We encode the error code in the message.
                    EncodeString($"[{((byte)errorCode).ToString(CultureInfo.InvariantCulture)}] {v.Message}");
                    break;
            }
        }

        // Other methods

        /// <summary>Encodes a ulong as a Slice varuint62 into a span of bytes using a fixed number of bytes.</summary>
        /// <param name="value">The value to encode.</param>
        /// <param name="into">The destination byte buffer, which must be 1, 2, 4 or 8 bytes long.</param>
        public static void EncodeVarUInt62(ulong value, Span<byte> into)
        {
            int sizeLength = into.Length;
            Debug.Assert(sizeLength == 1 || sizeLength == 2 || sizeLength == 4 || sizeLength == 8);

            (uint encodedSizeExponent, long maxSize) = sizeLength switch
            {
                1 => (0x00u, 63), // 2^6 - 1
                2 => (0x01u, 16_383), // 2^14 - 1
                4 => (0x02u, 1_073_741_823), // 2^30 - 1
                _ => (0x03u, (long)VarUInt62MaxValue)
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

        /// <summary>Computes the minimum number of bytes required to encode a long value using the Slice encoding's
        /// variable-size encoded representation.</summary>
        /// <param name="value">The long value.</param>
        /// <returns>The minimum number of bytes required to encode <paramref name="value"/>. Can be 1, 2, 4 or 8.
        /// </returns>
        public static int GetVarInt62EncodedSize(long value) => 1 << GetVarInt62EncodedSizeExponent(value);

        /// <summary>Computes the minimum number of bytes required to encode a ulong value using the Slice encoding's
        /// variable-size encoded representation.</summary>
        /// <param name="value">The ulong value.</param>
        /// <returns>The minimum number of bytes required to encode <paramref name="value"/>. Can be 1, 2, 4 or 8.
        /// </returns>
        public static int GetVarUInt62EncodedSize(ulong value) => 1 << GetVarUInt62EncodedSizeExponent(value);

        /// <summary>Encodes a non-null Slice2 encoded tagged value. The number of bytes needed to encode the value is
        /// not known before encoding this value (Slice2 only).</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The value to encode.</param>
        /// <param name="encodeAction">The delegate that encodes the value after the tag header.</param>
        public void EncodeTagged<T>(int tag, T v, EncodeAction<T> encodeAction) where T : notnull
        {
            if (Encoding == SliceEncoding.Slice1)
            {
                throw new InvalidOperationException("Slice1 encoded tags must be encoded with tag formats");
            }

            EncodeVarInt32(tag); // the key
            Span<byte> sizePlaceholder = GetPlaceholderSpan(4);
            int startPos = EncodedByteCount;
            encodeAction(ref this, v);
            EncodeVarUInt62((ulong)(EncodedByteCount - startPos), sizePlaceholder);
        }

        /// <summary>Encodes a non-null Slice2 encoded tagged value. The number of bytes needed to encode the value is
        /// known before encoding the value (Slice2 only).</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="size">The number of bytes needed to encode the value.</param>
        /// <param name="v">The value to encode.</param>
        /// <param name="encodeAction">The delegate that encodes the value after the tag header.</param>
        public void EncodeTagged<T>(int tag, int size, T v, EncodeAction<T> encodeAction) where T : notnull
        {
            if (Encoding == SliceEncoding.Slice1)
            {
                throw new InvalidOperationException("Slice1 encoded tags must be encoded with tag formats");
            }
            Debug.Assert(size > 0);

            EncodeVarInt32(tag); // the key
            EncodeSize(size);
            int startPos = EncodedByteCount;
            encodeAction(ref this, v);

            int actualSize = EncodedByteCount - startPos;
            if (actualSize != size)
            {
                throw new ArgumentException($"value of size ({size}) does not match encoded size ({actualSize})",
                                            nameof(size));
            }
        }

        /// <summary>Encodes a non-null Slice1 encoded tagged value. The number of bytes needed to encode the value is
        /// not known before encoding this value.</summary>
        /// <param name="tag">The tag. Must be either FSize or OVSize.</param>
        /// <param name="tagFormat">The tag format.</param>
        /// <param name="v">The value to encode.</param>
        /// <param name="encodeAction">The delegate that encodes the value after the tag header.</param>
        /// <exception cref="ArgumentException">Thrown if <paramref name="tagFormat"/> is not FSize or OVSize.
        /// </exception>
        public void EncodeTagged<T>(
            int tag,
            TagFormat tagFormat,
            T v,
            EncodeAction<T> encodeAction) where T : notnull
        {
            if (Encoding != SliceEncoding.Slice1)
            {
                throw new InvalidOperationException("tag formats can only be used with the Slice1 encoding");
            }

            switch (tagFormat)
            {
                case TagFormat.FSize:
                    EncodeTaggedParamHeader(tag, tagFormat);
                    Span<byte> placeholder = GetPlaceholderSpan(4);
                    int startPos = EncodedByteCount;
                    encodeAction(ref this, v);
                    // We don't include the size-length in the size we encode.
                    EncodeInt32(EncodedByteCount - startPos, placeholder);
                    break;

                case TagFormat.OVSize:
                    // A VSize where the size is optimized out. Used here for strings (and only strings) because we
                    // cannot easily compute the number of UTF-8 bytes in a C# string before encoding it.
                    EncodeTaggedParamHeader(tag, TagFormat.VSize);
                    encodeAction(ref this, v);
                    break;

                default:
                    throw new ArgumentException($"invalid value {tagFormat}", nameof(tagFormat));
            }
        }

        /// <summary>Encodes a non-null Slice1 encoded tagged value. The number of bytes needed to encode the
        /// value is known before encoding the value.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="tagFormat">The tag format.</param>
        /// <param name="size">The number of bytes needed to encode the value.</param>
        /// <param name="v">The value to encode.</param>
        /// <param name="encodeAction">The delegate that encodes the value after the tag header.</param>
        /// <exception cref="ArgumentException">Thrown if <paramref name="tagFormat"/> is FSize.</exception>
        public void EncodeTagged<T>(
            int tag,
            TagFormat tagFormat,
            int size,
            T v,
            EncodeAction<T> encodeAction) where T : notnull
        {
            if (Encoding != SliceEncoding.Slice1)
            {
                throw new InvalidOperationException("tag formats can only be used with the Slice1 encoding");
            }
            if (tagFormat == TagFormat.FSize)
            {
                throw new ArgumentException($"invalid value {tagFormat}", nameof(tagFormat));
            }
            Debug.Assert(size > 0);

            EncodeTaggedParamHeader(tag, tagFormat == TagFormat.OVSize ? TagFormat.VSize : tagFormat);

            if (tagFormat == TagFormat.VSize)
            {
                EncodeSize(size);
            }

            int startPos = EncodedByteCount;
            encodeAction(ref this, v);

            int actualSize = EncodedByteCount - startPos;
            if (actualSize != size)
            {
                throw new ArgumentException(
                    $"value of size ({size}) does not match encoded size ({actualSize})",
                    nameof(size));
            }
        }

        /// <summary>Allocates a new bit sequence in the underlying buffer(s) and returns a writer for this bit
        /// sequence.</summary>
        /// <param name="bitSequenceSize">The minimum number of bits in the bit sequence.</param>
        /// <returns>The bit sequence writer.</returns>
        public BitSequenceWriter GetBitSequenceWriter(int bitSequenceSize)
        {
            if (Encoding == SliceEncoding.Slice1)
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

                int remaining = GetBitSequenceByteCount(bitSequenceSize);

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

                return new BitSequenceWriter(firstSpan, secondSpan, additionalMemory);
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
        public int GetSizeLength(int size) => Encoding == SliceEncoding.Slice1 ?
            (size < 255 ? 1 : 5) : GetVarUInt62EncodedSize(checked((ulong)size));

        /// <summary>Copies a span of bytes to the underlying buffer writer.</summary>
        /// <param name="span">The span to copy.</param>
        public void WriteByteSpan(ReadOnlySpan<byte> span)
        {
            _bufferWriter.Write(span);
            EncodedByteCount += span.Length;
        }

        internal static int GetBitSequenceByteCount(int bitCount) => (bitCount >> 3) + ((bitCount & 0x07) != 0 ? 1 : 0);

        internal static void EncodeInt32(int v, Span<byte> into) => MemoryMarshal.Write(into, ref v);

        internal SliceEncoder(
            IBufferWriter<byte> bufferWriter,
            SliceEncoding encoding,
            FormatType classFormat = default)
            : this()
        {
            Encoding = encoding;
            _bufferWriter = bufferWriter;
            _classContext = new ClassContext(classFormat);
        }

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

        /// <summary>Gets the minimum number of bytes needed to encode a long value with the varint62 encoding as an
        /// exponent of 2.</summary>
        /// <param name="value">The value to encode.</param>
        /// <returns>N where 2^N is the number of bytes needed to encode value with Slice's varint62 encoding.</returns>
        private static int GetVarInt62EncodedSizeExponent(long value)
        {
            if (value < VarInt62MinValue || value > VarInt62MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"varint62 value '{value}' is out of range");
            }

            return (value << 2) switch
            {
                long b when b >= sbyte.MinValue && b <= sbyte.MaxValue => 0,
                long s when s >= short.MinValue && s <= short.MaxValue => 1,
                long i when i >= int.MinValue && i <= int.MaxValue => 2,
                _ => 3
            };
        }

        /// <summary>Gets the mimimum number of bytes needed to encode a ulong value with the varuint62 encoding as an
        /// exponent of 2.</summary>
        /// <param name="value">The value to encode.</param>
        /// <returns>N where 2^N is the number of bytes needed to encode value with Slice's varuint62 encoding.</returns>
        private static int GetVarUInt62EncodedSizeExponent(ulong value)
        {
            if (value > VarUInt62MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"varuint62 value '{value}' is out of range");
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

        /// <summary>Encodes an endpoint in a nested encapsulation (Slice1 only).</summary>
        /// <param name="endpoint">The endpoint to encode.</param>
        private void EncodeEndpoint(Endpoint endpoint)
        {
            Debug.Assert(Encoding == SliceEncoding.Slice1);

            // If there is no transport parameter, we default to TCP.
            if (!endpoint.Params.TryGetValue("transport", out string? transport))
            {
                transport = TransportNames.Tcp;
            }

            // The Slice1 encoding of ice endpoints is transport-specific, and hard-coded here. The preferred and
            // fallback encoding for new transports is TransportCode.Uri.

            if (endpoint.Protocol == Protocol.Ice && transport == TransportNames.Opaque)
            {
                // Opaque endpoint encoding

                (TransportCode transportCode, byte encodingMajor, byte encodingMinor, ReadOnlyMemory<byte> bytes) =
                    endpoint.ParseOpaqueParams();

                this.EncodeTransportCode(transportCode);
                EncodeInt32(4 + 2 + bytes.Length); // encapsulation size includes size-length and 2 bytes for encoding
                EncodeUInt8(encodingMajor);
                EncodeUInt8(encodingMinor);
                WriteByteSpan(bytes.Span);
            }
            else
            {
                TransportCode transportCode = endpoint.Protocol == Protocol.Ice ?
                    transport switch
                    {
                        TransportNames.Ssl => TransportCode.Ssl,
                        TransportNames.Tcp => TransportCode.Tcp,
                        _ => TransportCode.Uri
                    } :
                    TransportCode.Uri;

                this.EncodeTransportCode(transportCode);

                int startPos = EncodedByteCount; // size includes size-length
                Span<byte> sizePlaceholder = GetPlaceholderSpan(4); // encapsulation size
                EncodeUInt8(1); // encoding version major
                EncodeUInt8(1); // encoding version minor

                switch (transportCode)
                {
                    case TransportCode.Tcp:
                    case TransportCode.Ssl:
                        Transports.TcpClientTransport.EncodeEndpoint(ref this, endpoint);
                        break;

                    default:
                        Debug.Assert(transportCode == TransportCode.Uri);
                        EncodeString(endpoint.ToString());
                        break;
                }

                EncodeInt32(EncodedByteCount - startPos, sizePlaceholder);
            }
        }

        /// <summary>Encodes the header for a tagged parameter or data member. Slice1 only.</summary>
        /// <param name="tag">The numeric tag associated with the parameter or data member.</param>
        /// <param name="format">The tag format.</param>
        private void EncodeTaggedParamHeader(int tag, TagFormat format)
        {
            Debug.Assert(Encoding == SliceEncoding.Slice1);
            Debug.Assert(format != TagFormat.OVSize); // OVSize cannot be encoded

            int v = (int)format;
            if (tag < 30)
            {
                v |= tag << 3;
                EncodeUInt8((byte)v);
            }
            else
            {
                v |= 0x0F0; // tag = 30
                EncodeUInt8((byte)v);
                EncodeSize(tag);
            }

            if (_classContext.Current.InstanceType != InstanceType.None)
            {
                _classContext.Current.SliceFlags |= SliceFlags.HasTaggedMembers;
            }
        }
    }
}
