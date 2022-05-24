// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.RequestContext;

namespace IceRpc.Configure;

/// <summary>This class provide extension methods to add request context middleware to a <see cref="Router"/>
/// </summary>
public static class RequestContextRouterExtensions
{
    /// <summary>Adds a <see cref="RequestContext"/> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    public static Router UseRequestContext(this Router router) =>
        router.Use(next => new RequestContextMiddleware(next));
}
