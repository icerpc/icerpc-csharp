// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc.Internal
{
    /// <summary>Encoder for the Ice 2.0 encoding.</summary>
    internal class Ice20Encoder : IceEncoder
    {
        public override void EncodeClass(AnyClass v) =>
            throw new NotSupportedException("cannot encode a class with the Ice 2.0 encoding");

        public override void EncodeException(RemoteException v)
        {
            EncodeString(v.Message);
            v.Origin.Encode(this);
            v.Encode(this);
        }
        public override void EncodeNullableClass(AnyClass? v) =>
            throw new NotSupportedException("cannot encode a class with the Ice 2.0 encoding");

        public override void EncodeNullableProxy(Proxy? proxy)
        {
            if (proxy == null)
            {
                ProxyData20 proxyData = default;
                proxyData.Encode(this);
            }
            else
            {
                EncodeProxy(proxy);
            }
        }

        public override void EncodeProxy(Proxy proxy)
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

        public override void EncodeSize(int v) => EncodeVarULong((ulong)v);

        public override int GetSizeLength(int size) => Ice20Encoder.GetSizeLength(size);

        public override void IceEndException()
        {
        }

        public override void IceEndSlice(bool lastSlice) =>
            throw new NotSupportedException("cannot encode a class with the Ice 2.0 encoding");

        public override void IceEndDerivedExceptionSlice() =>
            throw new NotSupportedException("cannot encode a derived exception with the Ice 2.0 encoding");

        public override void IceStartDerivedExceptionSlice(string typeId, RemoteException exception) =>
            throw new NotSupportedException("cannot encode a derived exception with the Ice 2.0 encoding");

        public override void IceStartException(string typeId, RemoteException exception) => EncodeString(typeId);

        public override void IceStartFirstSlice(
            string[] allTypeIds,
            SlicedData? slicedData = null,
            int? compactTypeId = null) =>
            throw new NotSupportedException("cannot encode a class with the Ice 2.0 encoding");

        public override void IceStartNextSlice(string typeId, int? compactId = null) =>
            throw new NotSupportedException("cannot encode a class with the Ice 2.0 encoding");

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

        internal Ice20Encoder(BufferWriter bufferWriter)
            : base(bufferWriter)
        {
        }

        internal void EncodeField<T>(int key, T value, EncodeAction<T> encodeAction)
        {
            EncodeVarInt(key);
            BufferWriter.Position pos = StartFixedLengthSize(2); // 2-bytes size place holder
            encodeAction(this, value);
            EndFixedLengthSize(pos, 2);
        }

        internal override void EncodeSlicedData(SlicedData slicedData, string[] baseTypeIds) =>
            throw new NotSupportedException("cannot encode a class with the Ice 2.0 encoding");

        private protected override void EncodeFixedLengthSize(int size, Span<byte> into) =>
            into.EncodeFixedLengthSize20(size);

        private protected override void EncodeTaggedParamHeader(int tag, EncodingDefinitions.TagFormat format)
        {
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
