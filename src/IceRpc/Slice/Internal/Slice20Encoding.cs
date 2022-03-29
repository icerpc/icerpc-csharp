// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>The Slice 2.0 encoding class.</summary>
    internal sealed class Slice20Encoding : SliceEncoding
    {
        /// <summary>The Slice 2.0 encoding singleton.</summary>
        internal static SliceEncoding Instance { get; } = new Slice20Encoding();

        private Slice20Encoding()
            : base(Slice20Name)
        {
        }
    }
}
