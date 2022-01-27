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

        /// <summary>Decodes this instance by decoding its data members using the <see cref="SliceDecoder"/>.
        /// </summary>
        /// <param name="decoder">The Slice decoder.</param>
        protected abstract void IceDecode(ref SliceDecoder decoder);

        /// <summary>Encodes this instance by encoding its data members to the <see cref="SliceEncoder"/>.</summary>
        /// <param name="encoder">The Slice 1.1 encoder.</param>
        protected abstract void IceEncode(ref SliceEncoder encoder);

        internal void Decode(ref SliceDecoder decoder) => IceDecode(ref decoder);
        internal void Encode(ref SliceEncoder encoder) => IceEncode(ref encoder);
    }
}
