// Copyright (c) ZeroC, Inc.

using AuthorizationExample;

namespace IceRpc;

/// <summary>This class provides extension methods to add session middleware to a <see cref="Router" />.</summary>
internal static class RouterExtensions
{
    /// <summary>Adds a <see cref="HasSessionMiddleware" /> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <returns>The router being configured.</returns>
    internal static Router UseHasSession(this Router router) =>
        router.Use(next => new HasSessionMiddleware(next));

    /// <summary>Adds a <see cref="LoadSessionMiddleware" /> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="tokenStore">The session token store.</param>
    /// <returns>The router being configured.</returns>
    internal static Router UseLoadSession(this Router router, TokenStore tokenStore) =>
        router.Use(next => new LoadSessionMiddleware(next, tokenStore));
}
