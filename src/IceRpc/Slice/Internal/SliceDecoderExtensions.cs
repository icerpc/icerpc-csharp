// Copyright (c) ZeroC, Inc.

using Slice;

namespace IceRpc.Slice.Internal;

/// <summary>Provides extension methods for Slice decoder.</summary>
internal static class SliceDecoderExtensions
{
    /// <summary>Verifies the Slice decoder has reached the end of its underlying buffer.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    internal static void CheckEndOfBuffer(this ref SliceDecoder decoder)
    {
        if (!decoder.End)
        {
            throw new InvalidDataException($"There are {decoder.Remaining} bytes remaining in the buffer.");
        }
    }

    /// <summary>Skips any remaining tagged parameter, including the tag end marker (if applicable).</summary>
    /// <param name="decoder">The Slice decoder.</param>
    internal static void SkipTaggedParams(this ref SliceDecoder decoder) =>
        decoder.SkipTagged(useTagEndMarker: decoder.Encoding != SliceEncoding.Slice1);
}
