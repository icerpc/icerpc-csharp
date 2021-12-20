// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Slice
{
    /// <summary>UnknownSlicedClass represents a fully sliced class instance. The local IceRPC runtime does not know
    /// this type or any of its base classes (other than AnyClass).</summary>
    public sealed class UnknownSlicedClass : AnyClass
    {
        /// <inheritdoc/>
        protected override ImmutableList<SliceInfo> IceUnknownSlices { get; set; } = ImmutableList<SliceInfo>.Empty;

        /// <inheritdoc/>
        protected override void IceDecode(IceDecoder decoder)
        {
        }

        /// <inheritdoc/>
        protected override void IceEncode(Ice11Encoder encoder) =>
            encoder.EncodeUnknownSlices(UnknownSlices, fullySliced: true);

        internal UnknownSlicedClass()
        {
        }
    }
}
