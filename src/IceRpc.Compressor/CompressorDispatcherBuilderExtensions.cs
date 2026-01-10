// Copyright (c) ZeroC, Inc.

using IceRpc.Compressor;
using System.IO.Compression;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IDispatcherBuilder" /> to add the compressor middleware.
/// </summary>
public static class CompressorDispatcherBuilderExtensions
{
    /// <summary>Extension methods for <see cref="IDispatcherBuilder" />.</summary>
    /// <param name="builder">The builder being configured.</param>
    extension(IDispatcherBuilder builder)
    {
        /// <summary>Adds a <see cref="CompressorMiddleware" /> to this dispatcher builder.</summary>
        /// <param name="compressionFormat">The compression format for the compress operation.</param>
        /// <param name="compressionLevel">The compression level for the compress operation.</param>
        /// <returns>The builder being configured.</returns>
        public IDispatcherBuilder UseCompressor(
            CompressionFormat compressionFormat,
            CompressionLevel compressionLevel = CompressionLevel.Fastest) =>
            builder.Use(next => new CompressorMiddleware(next, compressionFormat, compressionLevel));
    }
}
