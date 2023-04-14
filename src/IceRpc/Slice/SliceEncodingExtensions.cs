// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Provides an extension method for <see cref="SliceEncoding" />.</summary>
public static class SliceEncodingExtensions
{
    private static readonly ReadOnlySequence<byte> _sizeZeroPayload = new(new byte[] { 0 });

    /// <summary>Creates a non-empty payload with size 0.</summary>
    /// <param name="encoding">The Slice encoding.</param>
    /// <returns>A non-empty payload with size 0.</returns>
    public static PipeReader CreateSizeZeroPayload(this SliceEncoding encoding) =>
        encoding != SliceEncoding.Slice1 ? PipeReader.Create(_sizeZeroPayload) :
            throw new NotSupportedException(
                $"{nameof(CreateSizeZeroPayload)} is only available for stream-capable Slice encodings.");
}
