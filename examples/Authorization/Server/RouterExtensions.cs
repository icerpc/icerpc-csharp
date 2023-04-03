// Copyright (c) ZeroC, Inc.

using AuthorizationExample;

namespace IceRpc;

/// <summary>This class provides extension methods to add an <see cref="AuthorizationMiddleware" /> and <see
/// cref="AuthenticationMiddleware/> to a <see cref="Router" />.</summary>
internal static class RouterExtensions
{
    /// <summary>Adds an <see cref="AuthenticationMiddleware" /> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="authenticationBearerScheme">The bearer authentication handler to decode the identity token.</param>
    /// <returns>The router being configured.</returns>
    internal static Router UseAuthentication(this Router router, IBearerAuthenticationHandler authenticationBearerScheme) =>
        router.Use(next => new AuthenticationMiddleware(next, authenticationBearerScheme));

    /// <summary>Adds an <see cref="AuthorizationMiddleware" /> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="authorizeFunc">The function called by the middleware to check if the request is authorized.</param>
    /// <returns>The router being configured.</returns>
    internal static Router UseAuthorization(this Router router, Func<IIdentityFeature, bool> authorizeFunc) =>
        router.Use(next => new AuthorizationMiddleware(next, authorizeFunc));
}
