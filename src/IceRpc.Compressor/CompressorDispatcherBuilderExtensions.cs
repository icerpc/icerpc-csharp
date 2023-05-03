// Copyright (c) ZeroC, Inc.

using IceRpc.Builder;
using IceRpc.Compressor;
using System.IO.Compression;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method to add the compressor middleware to an <see cref="IDispatcherBuilder" />.
/// </summary>
public static class CompressorDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="CompressorMiddleware" /> to this dispatcher builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="compressionFormat">The compression format for the compress operation.</param>
    /// <param name="compressionLevel">The compression level for the compress operation.</param>
    /// <returns>The builder being configured.</returns>
    public static IDispatcherBuilder UseCompressor(
        this IDispatcherBuilder builder,
        CompressionFormat compressionFormat,
        CompressionLevel compressionLevel = CompressionLevel.Fastest) =>
        builder.Use(next => new CompressorMiddleware(next, compressionFormat, compressionLevel));
}
