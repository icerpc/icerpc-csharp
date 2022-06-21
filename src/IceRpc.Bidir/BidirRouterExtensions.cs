// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Bidir;

namespace IceRpc;

/// <summary>This class provides extension methods to add the bidir middleware to a <see cref="Router"/>.
/// </summary>
public static class BidirRouterExtensions
{
    /// <summary>Adds a <see cref="BidirMiddleware"/> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="reconnectTimeout">The timeout for reestablish the connection.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseBidir(this Router router, TimeSpan reconnectTimeout) =>
        router.Use(next => new BidirMiddleware(next, reconnectTimeout));
}
