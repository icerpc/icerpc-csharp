// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>Base class for classes defined in Slice.</summary>
    /// <remarks>This class is part of the Slice engine code.</remarks>
    public abstract class AnyClass
    {
        /// <summary>Returns the unknown slices if the class has a preserved-slice base class and has been sliced-off
        /// during decoding.</summary>
        public virtual ImmutableList<SliceInfo> UnknownSlices
        {
            get => ImmutableList<SliceInfo>.Empty;
            set
            {
                // the default implementation has no underlying field.
            }
        }

        /// <summary>Decodes the properties of this instance.</summary>
        /// <param name="decoder">The Slice decoder.</param>
        protected abstract void DecodeCore(ref SliceDecoder decoder);

        /// <summary>Encodes the properties of this instance.</summary>
        /// <param name="encoder">The Slice encoder.</param>
        protected abstract void EncodeCore(ref SliceEncoder encoder);

        internal void Decode(ref SliceDecoder decoder) => DecodeCore(ref decoder);
        internal void Encode(ref SliceEncoder encoder) => EncodeCore(ref encoder);
    }
}
