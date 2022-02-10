// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>Provides an extension method for decoding a fragment.</summary>
    internal static class SliceDecoderFragmentExtensions
    {
        internal static string DecodeFragment(this ref SliceDecoder decoder) =>
            decoder.DecodeSize() switch
            {
                0 => "",
                1 => Uri.EscapeDataString(decoder.DecodeString()),
                _ => throw new InvalidDataException("received a Fragment with too many sequence elements")
            };
    }
}
