// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to decode a
/// <c>WellKnownTypes::Uuid</c> into a <see cref="Guid" />.</summary>
public static class UuidSliceDecoderExtensions
{
    /// <summary>Decodes a UUID.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The Uuid decoded as a <see cref="Guid"/>.</returns>
    public static Guid DecodeUuid(this ref SliceDecoder decoder)
    {
        using IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(16);
        Span<byte> data = owner.Memory.Span[..16];
        decoder.CopyTo(data);
        return new Guid(data);
    }
}
