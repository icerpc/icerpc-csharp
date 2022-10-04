// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Deflate;
using System.IO.Compression;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the deflate middleware to a <see cref="IDispatcherBuilder" />.
/// </summary>
public static class DeflateDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="DeflateMiddleware" /> to this dispatcher builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="compressionLevel">The compression level for the compress operation.</param>
    /// <returns>The builder being configured.</returns>
    public static IDispatcherBuilder UseDeflate(
        this IDispatcherBuilder builder,
        CompressionLevel compressionLevel = CompressionLevel.Fastest) =>
        builder.Use(next => new DeflateMiddleware(next, compressionLevel));
}
