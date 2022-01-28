// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Slice
{
    /// <summary>UnknownSlicedClass represents a fully sliced class instance. The local IceRPC runtime does not know
    /// this type or any of its base classes (other than AnyClass).</summary>
    public sealed class UnknownSlicedClass : AnyClass
    {
        /// <inheritdoc/>
        public override ImmutableList<SliceInfo> UnknownSlices { get; set; } = ImmutableList<SliceInfo>.Empty;

        /// <inheritdoc/>
        protected override void DecodeCore(ref SliceDecoder decoder)
        {
        }

        /// <inheritdoc/>
        protected override void EncodeCore(ref SliceEncoder encoder) =>
            encoder.EncodeUnknownSlices(UnknownSlices, fullySliced: true);

        internal UnknownSlicedClass()
        {
        }
    }
}
