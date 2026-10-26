// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Codec;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to decode a <c>WellKnownTypes::Uuid</c> into a
/// <see cref="Guid" />.</summary>
public static class UuidSliceDecoderExtensions
{
    /// <summary>Decodes a <c>WellKnownTypes::Uuid</c>.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The Uuid decoded as a <see cref="Guid"/>.</returns>
    /// <remarks>The 16 bytes are read in the network byte order specified by RFC 9562, not in the mixed-endian layout
    /// of <see cref="Guid.ToByteArray()" />.</remarks>
    public static Guid DecodeUuid(this ref SliceDecoder decoder)
    {
        Span<byte> span = stackalloc byte[16];
        decoder.CopyTo(span);
        return new Guid(span, bigEndian: true);
    }
}
