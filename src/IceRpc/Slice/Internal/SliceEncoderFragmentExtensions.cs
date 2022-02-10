// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>Provides an extension method for encoding a fragment.</summary>
    internal static class SliceEncoderFragmentExtensions
    {
        internal static void EncodeFragment(this ref SliceEncoder encoder, string value)
        {
            // encoded as a sequence<string>
            if (value.Length == 0)
            {
                encoder.EncodeSize(0);
            }
            else
            {
                encoder.EncodeSize(1);
                encoder.EncodeString(Uri.UnescapeDataString(value));
            }
        }
    }
}
