// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>Encodes and decodes a facet sequence into a percent-escaped fragment.</summary>
    internal static class FragmentExtensions // TODO: see https://github.com/zeroc-ice/icerpc-csharp/issues/786
    {
        internal static string DecodeFragment(this ref SliceDecoder decoder) =>
            decoder.DecodeSize() switch
            {
                0 => "",
                1 => Uri.EscapeDataString(decoder.DecodeString()),
                _ => throw new InvalidDataException("received a Fragment with too many sequence elements")
            };

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
