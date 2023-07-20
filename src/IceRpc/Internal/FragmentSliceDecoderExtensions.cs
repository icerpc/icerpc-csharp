// Copyright (c) ZeroC, Inc.

using ZeroC.Slice;

namespace IceRpc.Internal;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to decode a fragment.</summary>
internal static class FragmentSliceDecoderExtensions
{
    internal static string DecodeFragment(this ref SliceDecoder decoder) =>
        decoder.DecodeSize() switch
        {
            0 => "",
            1 => Uri.EscapeDataString(decoder.DecodeString()),
            _ => throw new InvalidDataException("Received a Fragment with too many sequence elements.")
        };
}
