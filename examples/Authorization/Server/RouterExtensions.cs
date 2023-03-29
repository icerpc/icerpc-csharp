// Copyright (c) ZeroC, Inc.

using AuthorizationExample;
using System.Security.Cryptography;

namespace IceRpc;

/// <summary>This class provides extension methods to add an <see cref="AuthorizationMiddleware" /> and <see
/// cref="AuthenticationMiddleware/> to a <see cref="Router" />.</summary>
internal static class RouterExtensions
{
    /// <summary>Adds an <see cref="AuthenticationMiddleware" /> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="encryptionAlgorithm">The encryption algorithm used to encrypt the identity token.</param>
    /// <returns>The router being configured.</returns>
    internal static Router UseAuthentication(this Router router, SymmetricAlgorithm encryptionAlgorithm) =>
        router.Use(next => new AuthenticationMiddleware(next, encryptionAlgorithm));

    /// <summary>Adds an <see cref="AuthorizationMiddleware" /> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="authorizeFunc">The function called by the middleware to check if the request is authorized.</param>
    /// <returns>The router being configured.</returns>
    internal static Router UseAuthorization(this Router router, Func<IIdentityFeature, bool> authorizeFunc) =>
        router.Use(next => new AuthorizationMiddleware(next, authorizeFunc));
}
