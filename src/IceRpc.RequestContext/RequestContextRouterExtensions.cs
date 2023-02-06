// Copyright (c) ZeroC, Inc.

using IceRpc.RequestContext;

namespace IceRpc;

/// <summary>This class provides extension methods to add the request context middleware to a <see cref="Router" />.
/// </summary>
public static class RequestContextRouterExtensions
{
    /// <summary>Adds a <see cref="RequestContextMiddleware" /> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseRequestContext(this Router router) =>
        router.Use(next => new RequestContextMiddleware(next));
}
