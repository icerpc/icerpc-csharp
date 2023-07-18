// Copyright (c) ZeroC, Inc.

using ZeroC.Slice;

namespace IceRpc.Internal;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to check if the end of the decoder's
/// underlying buffer is reached.</summary>
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
}
