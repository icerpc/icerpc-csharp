// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to decode a <c>WellKnownTypes::Uuid</c> into a
/// <see cref="Guid" />.</summary>
public static class UuidSliceDecoderExtensions
{
    /// <summary>Extension methods for <see cref="SliceDecoder" />.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    extension(ref SliceDecoder decoder)
    {
        /// <summary>Decodes a <c>WellKnownTypes::Uuid</c>.</summary>
        /// <returns>The Uuid decoded as a <see cref="Guid"/>.</returns>
        public Guid DecodeUuid()
        {
            using IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(16);
            Span<byte> span = owner.Memory.Span[..16];
            decoder.CopyTo(span);
            return new Guid(span);
        }
    }
}
