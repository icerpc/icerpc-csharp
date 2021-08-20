// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IceRpc
{
    /// <summary>Encoder for the Ice 2.0 encoding.</summary>
    public class Ice20Encoder : IceEncoder
    {
        /// <inheritdoc/>
        public override void EncodeException(RemoteException v)
        {
            EncodeString(v.Message);
            v.Origin.Encode(this);

            // We slice-off the exception to its top Slice if it's a derived exception
            v.Encode(this);
        }

        /// <inheritdoc/>
        public override void EncodeNullableClass(AnyClass? v) =>
            throw new NotSupportedException("cannot encode a class with the Ice 2.0 encoding");

        /// <inheritdoc/>
        public override void EncodeNullableProxy(Proxy? proxy)
        {
            if (proxy == null)
            {
                ProxyData20 proxyData = default;
                proxyData.Encode(this);
            }
            else
            {
                if (proxy.Connection?.IsServer ?? false)
                {
                    throw new InvalidOperationException("cannot encode a proxy bound to a server connection");
                }

                var proxyData = new ProxyData20(
                    proxy.Path,
                    protocol: proxy.Protocol != Protocol.Ice2 ? proxy.Protocol : null,
                    encoding: proxy.Encoding == proxy.Protocol.GetEncoding() ? null : proxy.Encoding.ToString(),
                    endpoint: proxy.Endpoint is Endpoint endpoint && endpoint.Transport != TransportNames.Coloc ?
                        endpoint.ToEndpointData() : null,
                    altEndpoints:
                            proxy.AltEndpoints.Count == 0 ? null :
                                proxy.AltEndpoints.Select(e => e.ToEndpointData()).ToArray());

                proxyData.Encode(this);
            }
        }

        /// <inheritdoc/>
        public override void EncodeSize(int v) => EncodeVarULong((ulong)v);

        /// <inheritdoc/>
        public override int GetSizeLength(int size) => Ice20Encoder.GetSizeLength(size);

        /// <summary>Encodes a size into a span of bytes using a fixed number of bytes.</summary>
        /// <param name="size">The size to encode.</param>
        /// <param name="into">The destination byte buffer, which must be 1, 2, 4 or 8 bytes long.</param>
        internal static void EncodeFixedLengthSize(long size, Span<byte> into)
        {
            int sizeLength = into.Length;
            Debug.Assert(sizeLength == 1 || sizeLength == 2 || sizeLength == 4 || sizeLength == 8);

            (uint encodedSizeExponent, long maxSize) = sizeLength switch
            {
                1 => (0x00u, 63), // 2^6 - 1
                2 => (0x01u, 16_383), // 2^14 - 1
                4 => (0x02u, 1_073_741_823), // 2^30 - 1
                _ => (0x03u, (long)EncodingDefinitions.VarULongMaxValue)
            };

            if (size < 0 || size > maxSize)
            {
                throw new ArgumentOutOfRangeException(
                    $"size '{size}' cannot be encoded on {sizeLength} bytes",
                    nameof(size));
            }

            Span<byte> ulongBuf = stackalloc byte[8];
            ulong v = (ulong)size;
            v <<= 2;

            v |= encodedSizeExponent;
            MemoryMarshal.Write(ulongBuf, ref v);
            ulongBuf.Slice(0, sizeLength).CopyTo(into);
        }

        /// <summary>Computes the minimum number of bytes needed to encode a variable-length size with the 2.0 encoding.
        /// </summary>
        /// <remarks>The parameter is a long and not a varulong because sizes and size-like values are usually passed
        /// around as signed integers, even though sizes cannot be negative and are encoded like varulong values.
        /// </remarks>
        internal static int GetSizeLength(long size)
        {
            Debug.Assert(size >= 0);
            return 1 << GetVarULongEncodedSizeExponent((ulong)size);
        }

        /// <summary>Constructs an encoder for the Ice 2.0 encoding.</summary>
        internal Ice20Encoder(BufferWriter bufferWriter)
            : base(bufferWriter)
        {
        }

        internal void EncodeFixedLengthSize(int size, BufferWriter.Position pos, int sizeLength)
        {
            Debug.Assert(pos.Offset >= 0);
            Span<byte> data = stackalloc byte[sizeLength];
            EncodeFixedLengthSize(size, data);
            BufferWriter.RewriteByteSpan(data, pos);
        }

        internal void EncodeField<T>(int key, T value, EncodeAction<T> encodeAction)
        {
            EncodeVarInt(key);
            BufferWriter.Position pos = StartFixedLengthSize(2); // 2-bytes size place holder
            encodeAction(this, value);
            EndFixedLengthSize(pos, 2);
        }

        /// <summary>Computes the amount of data encoded from the start position to the current position and writes that
        /// size at the start position (as a fixed-length size). The size does not include its own encoded length.
        /// </summary>
        /// <param name="start">The start position.</param>
        /// <param name="sizeLength">The number of bytes used to encode the size 1, 2 or 4.</param>
        /// <returns>The size of the encoded data.</returns>
        internal int EndFixedLengthSize(BufferWriter.Position start, int sizeLength)
        {
            int size = BufferWriter.Distance(start) - sizeLength;
            EncodeFixedLengthSize(size, start, sizeLength);
            return size;
        }

        /// <summary>Returns the current position and writes placeholder for a fixed-length size value. The
        /// position must be used to rewrite the size later.</summary>
        /// <param name="sizeLength">The number of bytes reserved to encode the fixed-length size.</param>
        /// <returns>The position before writing the size.</returns>
        internal BufferWriter.Position StartFixedLengthSize(int sizeLength)
        {
            BufferWriter.Position pos = BufferWriter.Tail;
            BufferWriter.WriteByteSpan(stackalloc byte[sizeLength]); // placeholder for future size
            return pos;
        }

        private protected override void EncodeFixedLengthSize(int size, Span<byte> into) =>
            Ice20Encoder.EncodeFixedLengthSize(size, into);

        private protected override void EncodeTaggedParamHeader(int tag, EncodingDefinitions.TagFormat format)
        {
            // TODO: merge FSize and VSize

            Debug.Assert(format != EncodingDefinitions.TagFormat.VInt); // VInt cannot be encoded

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
        }
    }
}
