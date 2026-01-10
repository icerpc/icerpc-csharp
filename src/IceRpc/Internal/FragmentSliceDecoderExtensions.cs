// Copyright (c) ZeroC, Inc.

using ZeroC.Slice;

namespace IceRpc.Internal;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to decode a fragment.</summary>
internal static class FragmentSliceDecoderExtensions
{
    /// <summary>Extension methods for <see cref="SliceDecoder" />.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    extension(ref SliceDecoder decoder)
    {
        internal string DecodeFragment() =>
            decoder.DecodeSize() switch
            {
                0 => "",
                1 => Uri.EscapeDataString(decoder.DecodeString()),
                _ => throw new InvalidDataException("Received a Fragment with too many sequence elements.")
            };
    }
}
