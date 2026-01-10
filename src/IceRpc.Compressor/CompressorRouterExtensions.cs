// Copyright (c) ZeroC, Inc.

using IceRpc.Compressor;
using System.IO.Compression;

namespace IceRpc;

/// <summary>Provides an extension method for <see cref="Router" /> to add the compressor middleware.</summary>
public static class CompressorRouterExtensions
{
    /// <summary>Extension methods for <see cref="Router" />.</summary>
    /// <param name="router">The router being configured.</param>
    extension(Router router)
    {
        /// <summary>Adds a <see cref="CompressorMiddleware" /> to this router.</summary>
        /// <param name="compressionFormat">The compression format for the compress operation.</param>
        /// <param name="compressionLevel">The compression level for the compress operation.</param>
        /// <returns>The router being configured.</returns>
        /// <example>
        /// The following code adds the compressor middleware to the dispatch pipeline.
        /// <code source="../../docfx/examples/IceRpc.Compressor.Examples/CompressorMiddlewareExamples.cs"
        /// region="UseCompressor" lang="csharp" />
        /// </example>
        /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/main/examples/Compress"/>
        public Router UseCompressor(
            CompressionFormat compressionFormat,
            CompressionLevel compressionLevel = CompressionLevel.Fastest) =>
            router.Use(next => new CompressorMiddleware(next, compressionFormat, compressionLevel));
    }
}
