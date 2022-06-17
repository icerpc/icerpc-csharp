// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Jwt;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the request context interceptor to an
/// <see cref="IInvokerBuilder"/>.</summary>
public static class JwtInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="JwtInterceptor"/> to the builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseJwt(this IInvokerBuilder builder) =>
        builder.Use(next => new JwtInterceptor(next));
}
