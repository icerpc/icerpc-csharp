// Copyright (c) ZeroC, Inc.

using System.Diagnostics;

namespace ZeroC.Slice.Codec;

/// <summary>Provides an extension method for <see cref="SliceEncoder" /> to encode a <see cref="Guid" /> as a
/// <c>WellKnownTypes::Uuid</c>.</summary>
public static class UuidSliceEncoderExtensions
{
    /// <summary>Encodes a <see cref="Guid" /> as a <c>WellKnownTypes::Uuid</c>.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The value to encode.</param>
    /// <remarks>The 16 bytes are written in the network byte order specified by RFC 9562, not in the mixed-endian
    /// layout of <see cref="Guid.ToByteArray()" />.</remarks>
    public static void EncodeUuid(this ref SliceEncoder encoder, Guid value)
    {
        bool written = value.TryWriteBytes(encoder.GetPlaceholderSpan(16), bigEndian: true, out _);
        Debug.Assert(written);
    }
}
