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
        public ImmutableList<SliceInfo> UnknownSlices
        {
            get => IceUnknownSlices;
            internal set => IceUnknownSlices = value;
        }

        /// <summary>The implementation of <see cref="UnknownSlices"/>.</summary>
        protected virtual ImmutableList<SliceInfo> IceUnknownSlices
        {
            get => ImmutableList<SliceInfo>.Empty;
            set
            {
                // ignored, i.e. we don't store/preserve these unknown slices
            }
        }

        /// <summary>Decodes the properties of this instance.</summary>
        /// <param name="decoder">The Slice decoder.</param>
        public abstract void Decode(ref SliceDecoder decoder);

        /// <summary>Encodes the properties of this instance.</summary>
        /// <param name="encoder">The Slice encoder.</param>
        public abstract void Encode(ref SliceEncoder encoder);
    }
}
