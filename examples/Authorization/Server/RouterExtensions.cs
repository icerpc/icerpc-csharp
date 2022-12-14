// Copyright (c) ZeroC, Inc. All rights reserved.

using AuthorizationExample;

namespace IceRpc;

/// <summary>This class provides extension methods to add session middleware to a <see cref="Router" />.</summary>
public static class RouterExtensions
{
    /// <summary>Adds a <see cref="LoadSessionMiddleware" /> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="tokenStore">The session token store.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseLoadSession(this Router router, TokenStore tokenStore) =>
        router.Use(next => new LoadSessionMiddleware(next, tokenStore));

    /// <summary>Adds a <see cref="HasSessionMiddleware" /> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseHasSession(this Router router) =>
        router.Use(next => new HasSessionMiddleware(next));
}
