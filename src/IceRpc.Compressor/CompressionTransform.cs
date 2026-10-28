// Copyright (c) ZeroC, Inc.

using System.IO.Compression;

namespace IceRpc.Compressor;

/// <summary>Creates the stream transform that compresses a payload.</summary>
internal static class CompressionTransform
{
    // BrotliStream compresses each write on its own, and payloads arrive in 4 KB segments: buffering them into larger
    // writes brings the Brotli output within 2% of a single write, where 4 KB writes cost about 20% at
    // CompressionLevel.Fastest. Deflate's output does not depend on the write size.
    private const int BrotliBufferSize = 128 * 1024;

    internal static Func<Stream, Stream> Create(
        CompressionFormat compressionFormat,
        CompressionLevel compressionLevel) =>
        compressionFormat switch
        {
            CompressionFormat.Brotli =>
                stream => new BufferedStream(new BrotliStream(stream, compressionLevel), BrotliBufferSize),
            CompressionFormat.Deflate => stream => new DeflateStream(stream, compressionLevel),
            _ => throw new NotSupportedException($"The compression format '{compressionFormat}' is not supported."),
        };
}
