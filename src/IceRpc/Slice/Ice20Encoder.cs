// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IceRpc.Slice
{
    /// <summary>Encoder for the Ice 2.0 encoding.</summary>
    public sealed class Ice20Encoder : IceEncoder
    {
        /// <inheritdoc/>
        public override void EncodeException(RemoteException v) => v.Encode(this);

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
                    protocol: proxy.Protocol != Protocol.Ice2 ? proxy.Protocol.Code : null,
                    encoding: proxy.Encoding == proxy.Protocol.IceEncoding ? null : proxy.Encoding.ToString(),
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
        public override void EncodeTagged<T>(
            int tag,
            TagFormat tagFormat,
            T v,
            EncodeAction<IceEncoder, T> encodeAction)
        {
            EncodeVarInt(tag); // the key
            Span<byte> sizePlaceHolder = GetPlaceHolderSpan(4);
            int startPos = EncodedBytes;
            encodeAction(this, v);
            EncodeSize(EncodedBytes - startPos, sizePlaceHolder);
        }

        /// <inheritdoc/>
        public override void EncodeTagged<T>(
            int tag,
            TagFormat tagFormat,
            int size,
            T v,
            EncodeAction<IceEncoder, T> encodeAction)
        {
            EncodeVarInt(tag); // the key
            EncodeSize(size);
            int startPos = EncodedBytes;
            encodeAction(this, v);
            int actualSize = EncodedBytes - startPos;
            if (actualSize != size)
            {
                throw new ArgumentException($"value of size ({size}) does not match encoded size ({actualSize})",
                                            nameof(size));
            }
        }

        /// <inheritdoc/>
        public override int GetSizeLength(int size) => Ice20Encoder.GetSizeLength(size);

        /// <summary>Encodes a size into a span of bytes using a fixed number of bytes.</summary>
        /// <param name="size">The size to encode.</param>
        /// <param name="into">The destination byte buffer, which must be 1, 2, 4 or 8 bytes long.</param>
        internal static void EncodeSize(long size, Span<byte> into)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "size must be positive");
            }
            EncodeVarULong((ulong)size, into);
        }

        /// <summary>Computes the minimum number of bytes needed to encode a variable-length size with the 2.0 encoding.
        /// </summary>
        /// <remarks>The parameter is a long and not a varulong because sizes and size-like values are usually passed
        /// around as signed integers, even though sizes cannot be negative and are encoded like varulong values.
        /// </remarks>
        internal static int GetSizeLength(long size) => GetVarULongEncodedSize(checked((ulong)size));

        /// <summary>Constructs an encoder for the Ice 2.0 encoding.</summary>
        internal Ice20Encoder(IBufferWriter<byte> bufferWriter)
            : base(bufferWriter)
        {
        }

        internal void EncodeField<T>(int key, T value, EncodeAction<Ice20Encoder, T> encodeAction)
        {
            EncodeVarInt(key);
            Span<byte> sizePlaceHolder = GetPlaceHolderSpan(2);
            int startPos = EncodedBytes;
            encodeAction(this, value);
            EncodeSize(EncodedBytes - startPos, sizePlaceHolder);
        }

        internal override void EncodeFixedLengthSize(int size, Span<byte> into) => EncodeSize(size, into);
    }
}
