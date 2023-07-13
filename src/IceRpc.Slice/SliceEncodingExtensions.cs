// Copyright (c) ZeroC, Inc.

using Slice;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Provides an extension method for <see cref="SliceEncoding" />.</summary>
public static class SliceEncodingExtensions
{
    // 4 = varuint62 encoding of the size (1)
    // 252 = varint32 encoding of the Slice2 tag end marker (-1)
    private static readonly ReadOnlySequence<byte> _emptyStructPayload = new(new byte[] { 4, 252 });

    /// <summary>Creates the payload of an empty struct.</summary>
    /// <param name="encoding">The Slice encoding.</param>
    /// <returns>The payload of an empty struct.</returns>
    public static PipeReader CreateEmptyStructPayload(this SliceEncoding encoding) =>
        encoding != SliceEncoding.Slice1 ? PipeReader.Create(_emptyStructPayload) :
            throw new NotSupportedException(
                $"{nameof(CreateEmptyStructPayload)} is only available for stream-capable Slice encodings.");
}
