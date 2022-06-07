// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Deflate;
using System.IO.Compression;

namespace IceRpc;

/// <summary>This class provides extension methods to add the deflate interceptor to a <see cref="Pipeline"/>.
/// </summary>
public static class DeflatePipelineExtensions
{
    /// <summary>Adds a <see cref="DeflateInterceptor"/> to the pipeline.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    /// <param name="compressionLevel">The compression level for the compress operation.</param>
    /// <returns>The pipeline being configured.</returns>
    public static Pipeline UseDeflate(
        this Pipeline pipeline,
        CompressionLevel compressionLevel = CompressionLevel.Fastest) =>
        pipeline.Use(next => new DeflateInterceptor(next, compressionLevel));
}
