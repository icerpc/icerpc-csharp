// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceEncoder" /> to encode a <see cref="Guid" /> as a
/// <c>WellKnownTypes::Uuid</c>.</summary>
public static class UuidSliceEncoderExtensions
{
    /// <summary>Extension methods for <see cref="SliceEncoder" />.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    extension(ref SliceEncoder encoder)
    {
        /// <summary>Encodes a <see cref="Guid" /> as a <c>WellKnownTypes::Uuid</c>.</summary>
        /// <param name="value">The value to encode.</param>
        public void EncodeUuid(Guid value)
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
}
