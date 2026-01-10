// Copyright (c) ZeroC, Inc.

using ZeroC.Slice;

namespace IceRpc.Internal;

/// <summary>Provides an extension method for <see cref="SliceEncoder" /> to encode a fragment.</summary>
internal static class FragmentSliceEncoderExtensions
{
    /// <summary>Extension methods for <see cref="SliceEncoder" />.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    extension(ref SliceEncoder encoder)
    {
        internal void EncodeFragment(string value)
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
