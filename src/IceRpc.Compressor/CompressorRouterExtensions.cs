// Copyright (c) ZeroC, Inc.

using IceRpc.Compressor;
using System.IO.Compression;

namespace IceRpc;

/// <summary>Provides an extension method to add the compressor middleware to a <see cref="Router" />.</summary>
public static class CompressorRouterExtensions
{
    /// <summary>Adds a <see cref="CompressorMiddleware" /> to this router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="compressionFormat">The compression format for the compress operation.</param>
    /// <param name="compressionLevel">The compression level for the compress operation.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseCompressor(
        this Router router,
        CompressionFormat compressionFormat,
        CompressionLevel compressionLevel = CompressionLevel.Fastest) =>
        router.Use(next => new CompressorMiddleware(next, compressionFormat, compressionLevel));
}
