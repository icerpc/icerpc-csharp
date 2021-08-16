// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc.Internal
{
    /// <summary>.</summary>
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

        internal override void EncodeSlicedData(SlicedData slicedData, string[] baseTypeIds) =>
            throw new NotSupportedException("cannot encode a class with the Ice 2.0 encoding");

        /// <inheritdoc/>
        private protected override void EncodeNullProxy()
        {
            ProxyData20 proxyData = default;
            proxyData.Encode(this);
        }

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
