// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;

namespace IceRpc.Internal;

/// <summary>Provides an extension method for <see cref="IceDecoder" /> to check if the end of the decoder's
/// underlying buffer is reached.</summary>
internal static class IceDecoderExtensions
{
    /// <summary>Verifies the Ice decoder has reached the end of its underlying buffer.</summary>
    /// <param name="decoder">The Ice decoder.</param>
    internal static void CheckEndOfBuffer(this ref IceDecoder decoder)
    {
        if (!decoder.End)
        {
            throw new InvalidDataException($"There are {decoder.Remaining} bytes remaining in the buffer.");
        }
    }
}
