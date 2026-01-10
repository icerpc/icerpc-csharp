// Copyright (c) ZeroC, Inc.

using ZeroC.Slice;

namespace IceRpc.Internal;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to check if the end of the decoder's
/// underlying buffer is reached.</summary>
internal static class SliceDecoderExtensions
{
    /// <summary>Extension methods for <see cref="SliceDecoder" />.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    extension(ref SliceDecoder decoder)
    {
        /// <summary>Verifies the Slice decoder has reached the end of its underlying buffer.</summary>
        internal void CheckEndOfBuffer()
        {
            if (!decoder.End)
            {
                throw new InvalidDataException($"There are {decoder.Remaining} bytes remaining in the buffer.");
            }
        }
    }
}
