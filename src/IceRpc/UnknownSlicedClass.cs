// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>UnknownSlicedClass represents a fully sliced class instance. The local IceRPC runtime does not know
    /// this type or any of its base classes (other than AnyClass).</summary>
    public sealed class UnknownSlicedClass : AnyClass
    {
        /// <summary>Returns the most derived type ID this class instance.</summary>
        /// <value>The type ID.</value>
        public string TypeId => UnknownSlices[0].TypeId;

        /// <inheritdoc/>
        protected override ImmutableList<SliceInfo> IceUnknownSlices { get; set; } =
            ImmutableList<SliceInfo>.Empty;

        /// <inheritdoc/>
        protected override void IceDecode(Ice11Decoder decoder, bool firstSlice) =>
            UnknownSlices = decoder.UnknownSlices;

        /// <inheritdoc/>
        protected override void IceEncode(Ice11Encoder encoder, bool firstSlice) =>
            encoder.EncodeUnknownSlices(UnknownSlices, fullySliced: true);

        internal UnknownSlicedClass()
        {
        }
    }
}
