// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace IceRpc;

/// <summary>This class provides extension methods to add the JWT middleware to a <see cref="Router"/>.</summary>
public static class JwtRouterExtensions
{
    /// <summary>Adds a <see cref="JwtMiddleware"/> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="validationParameters">The parameters used to validate the JWT token.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseJwt(this Router router, TokenValidationParameters validationParameters) =>
        router.Use(next => new JwtMiddleware(next, validationParameters));

    /// <summary>Adds a <see cref="JwtMiddleware"/> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseJwt(this Router router) =>
        router.Use(next => new JwtMiddleware(next));
}
