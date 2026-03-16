// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace IceRpc.Ice.Codec;

/// <summary>Provides an extension method for <see cref="IceEncoder" /> to encode a <see cref="Guid" /> as a
/// <c>WellKnownTypes::Uuid</c>.</summary>
public static class UuidIceEncoderExtensions
{
    /// <summary>Encodes a <see cref="Guid" /> as a <c>WellKnownTypes::Uuid</c>.</summary>
    /// <param name="encoder">The Ice encoder.</param>
    /// <param name="value">The value to encode.</param>
    public static void EncodeUuid(this ref IceEncoder encoder, Guid value)
    {
        using IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(16);
        Span<byte> span = owner.Memory.Span[..16];
        if (!value.TryWriteBytes(span))
        {
            throw new InvalidOperationException($"Failed to encode UUID '{value}'.");
        }

        encoder.WriteByteSpan(span);
    }
}
