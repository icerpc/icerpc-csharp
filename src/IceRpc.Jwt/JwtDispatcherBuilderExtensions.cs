// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the request context middleware to a
/// <see cref="IDispatcherBuilder"/>.</summary>
public static class RequestContextDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="JwtMiddleware"/> to this dispatcher builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="validationParameters">The parameters used to validate the Jwt token.</param>
    /// <returns>The builder being configured.</returns>
    public static IDispatcherBuilder UseJwt(
        this IDispatcherBuilder builder,
        TokenValidationParameters validationParameters) =>
        builder.Use(next => new JwtMiddleware(next, validationParameters));
}
