// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Compressor;
using System.IO.Compression;

namespace IceRpc;

/// <summary>This class provides extension methods to add the compressor interceptor to a <see cref="Pipeline" />.
/// </summary>
public static class CompressorPipelineExtensions
{
    /// <summary>Adds a <see cref="CompressorInterceptor" /> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="compressionFormat">The compression format for the compress operation.</param>
    /// <param name="compressionLevel">The compression level for the compress operation.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseCompressor(
        this Pipeline pipeline,
        CompressionFormat compressionFormat,
        CompressionLevel compressionLevel = CompressionLevel.Fastest) =>
        pipeline.Use(next => new CompressorInterceptor(next, compressionFormat, compressionLevel));
}
