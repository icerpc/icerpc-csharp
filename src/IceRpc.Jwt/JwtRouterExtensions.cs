// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace IceRpc;

/// <summary>This class provides extension methods to add the request context middleware to a <see cref="Router"/>.
/// </summary>
public static class RequestContextRouterExtensions
{
    /// <summary>Adds a <see cref="JwtMiddleware"/> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="validationParameters">The parameters used to validate the Jwt token.</param>
    public static Router UseJwt(this Router router, TokenValidationParameters validationParameters) =>
        router.Use(next => new JwtMiddleware(validationParameters, next));

    /// <summary>Adds a <see cref="JwtMiddleware"/> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    public static Router UseJwt(this Router router) =>
        router.Use(next => new JwtMiddleware(next));
}
