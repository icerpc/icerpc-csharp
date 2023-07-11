// Copyright (c) ZeroC, Inc.

using Slice;

namespace IceRpc.Slice.Internal;

/// <summary>Provides an extension method for Slice decoder.</summary>
internal static class SliceDecoderExtensions
{
    /// <summary>Verifies the Slice decoder has reached the end of its underlying buffer.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <param name="skipTaggedParams">When <see langword="true" />, first skips all remaining tagged parameters in the
    /// current buffer.</param>
    internal static void CheckEndOfBuffer(this ref SliceDecoder decoder, bool skipTaggedParams)
    {
        if (skipTaggedParams)
        {
            decoder.SkipTagged(useTagEndMarker: false);
        }

        if (!decoder.End)
        {
            throw new InvalidDataException($"There are {decoder.Remaining} bytes remaining in the buffer.");
        }
    }
}
