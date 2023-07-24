// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceEncoder" /> to encode a <see cref="Guid" /> as a
/// <c>WellKnownTypes::Uuid</c>.</summary>
public static class UuidSliceEncoderExtensions
{
    /// <summary>Encodes a <see cref="Guid" /> as a <c>WellKnownTypes::Uuid</c>.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The value to encode.</param>
    public static void EncodeUuid(this ref SliceEncoder encoder, Guid value)
    {
        Span<byte> span = encoder.GetPlaceholderSpan(16);
        if (!value.TryWriteBytes(span))
        {
            throw new InvalidOperationException($"Failed to encode Uuid '{value}'.");
        }
    }
}
