// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Deflate;
using System.IO.Compression;

namespace IceRpc.Configure;

/// <summary>This class provide extension methods to add deflate middleware to a <see cref="Router"/>
/// </summary>
public static class RouterExtensions
{
    /// <summary>Adds a <see cref="DeflateMiddleware"/> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="compressionLevel">The compression level for the compress operation.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseDeflate(
        this Router router,
        CompressionLevel compressionLevel = CompressionLevel.Fastest) =>
        router.Use(next => new DeflateMiddleware(next, compressionLevel));
}
