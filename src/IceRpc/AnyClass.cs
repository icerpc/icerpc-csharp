// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>The base class for classes defined in Slice.</summary>
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

        /// <summary>Decodes this instance by decoding its data members from the <see cref="IceDecoder"/>.
        /// </summary>
        /// <param name="decoder">The Ice decoder.</param>
        protected abstract void IceDecode(Ice11Decoder decoder);

        /// <summary>Encodes this instance by encoding its data to the <see cref="IceEncoder"/>.</summary>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="firstSlice"><c>True</c> if this is the first Slice otherwise<c>False</c>.</param>
        protected abstract void IceEncode(Ice11Encoder encoder, bool firstSlice);

        internal void Decode(Ice11Decoder decoder) => IceDecode(decoder);
        internal void Encode(Ice11Encoder encoder) => IceEncode(encoder, true);
    }
}
