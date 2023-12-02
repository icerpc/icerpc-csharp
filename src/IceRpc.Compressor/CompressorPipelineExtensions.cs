// Copyright (c) ZeroC, Inc.

using IceRpc.Compressor;
using System.IO.Compression;

namespace IceRpc;

/// <summary>Provides an extension method for <see cref="Pipeline" /> to add the compressor interceptor.</summary>
public static class CompressorPipelineExtensions
{
    /// <summary>Adds a <see cref="CompressorInterceptor" /> to this pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="compressionFormat">The compression format for the compress operation.</param>
    /// <param name="compressionLevel">The compression level for the compress operation.</param>
    /// <returns>The pipeline being configured.</returns>
    /// <example>
    /// The following code adds the compressor interceptor to the invocation pipeline.
    /// <code source="../../docfx/examples/IceRpc.Compressor.Examples/CompressorInterceptorExamples.cs" region="UseCompressor" lang="csharp" />
    /// </example>
    /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/0.1.x/examples/Compress"/>
    public static Pipeline UseCompressor(
        this Pipeline pipeline,
        CompressionFormat compressionFormat,
        CompressionLevel compressionLevel = CompressionLevel.Fastest) =>
        pipeline.Use(next => new CompressorInterceptor(next, compressionFormat, compressionLevel));
}
