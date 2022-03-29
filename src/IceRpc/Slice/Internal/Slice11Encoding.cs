// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>The Slice 1.1 encoding class.</summary>
    internal sealed class Slice11Encoding : SliceEncoding
    {
        /// <summary>The Slice 1.1 encoding singleton.</summary>
        internal static SliceEncoding Instance { get; } = new Slice11Encoding();

        private Slice11Encoding()
            : base(Slice11Name)
        {
        }
    }
}
