// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Deflate;
using System.IO.Compression;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the deflate interceptor to an <see cref="IInvokerBuilder" />.
/// </summary>
public static class DeflateInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="DeflateInterceptor" /> to the builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="compressionLevel">The compression level for the compress operation.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseDeflate(
        this IInvokerBuilder builder,
        CompressionLevel compressionLevel = CompressionLevel.Fastest) =>
        builder.Use(next => new DeflateInterceptor(next, compressionLevel));
}
