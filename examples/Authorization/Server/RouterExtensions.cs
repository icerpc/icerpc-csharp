// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using System.Security.Cryptography;

namespace IceRpc;

/// <summary>This class provides extension methods to add session middleware to a <see cref="Router" />.</summary>
internal static class RouterExtensions
{
    /// <summary>Adds a <see cref="HasSessionMiddleware" /> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <returns>The router being configured.</returns>
    internal static Router UseAuthorization(this Router router, Func<IAuthenticationFeature, bool> authorizeFunc) =>
        router.Use(next => new AuthorizationMiddleware(next, authorizeFunc));

    /// <summary>Adds a <see cref="LoadSessionMiddleware" /> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="tokenStore">The session token store.</param>
    /// <returns>The router being configured.</returns>
    internal static Router UseAuthentication(this Router router, SymmetricAlgorithm cryptAlgorithm) =>
        router.Use(next => new AuthenticationMiddleware(next, cryptAlgorithm));
}
