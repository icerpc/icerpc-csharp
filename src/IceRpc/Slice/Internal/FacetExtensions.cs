// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>Encodes and decodes a facet sequence into a percent-escaped fragment.</summary>
    internal static class FacetExtensions // TODO: see https://github.com/zeroc-ice/icerpc-csharp/issues/786
    {
        internal static string DecodeFacet(this ref SliceDecoder decoder) =>
            decoder.DecodeSize() switch
            {
                0 => "",
                1 => Uri.EscapeDataString(decoder.DecodeString()),
                _ => throw new InvalidDataException("received a facet sequence with too many elements")
            };

        internal static void EncodeFacet(this ref SliceEncoder encoder, string fragment)
        {
            // encoded as a sequence<string>
            if (fragment.Length == 0)
            {
                encoder.EncodeSize(0);
            }
            else
            {
                encoder.EncodeSize(1);
                encoder.EncodeString(Uri.UnescapeDataString(fragment));
            }
        }
    }
}
