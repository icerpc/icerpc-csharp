// Copyright (c) ZeroC, Inc.

using IceRpc.RequestContext;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IDispatcherBuilder" /> to add the request context middleware.
/// </summary>
public static class RequestContextDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="RequestContextMiddleware" /> to this dispatcher builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IDispatcherBuilder UseRequestContext(this IDispatcherBuilder builder) =>
        builder.Use(next => new RequestContextMiddleware(next));
}
