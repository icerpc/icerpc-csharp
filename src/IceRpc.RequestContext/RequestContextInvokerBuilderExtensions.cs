// Copyright (c) ZeroC, Inc.

using IceRpc.RequestContext;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IInvokerBuilder" /> to add the request context interceptor.
/// </summary>
public static class RequestContextInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="RequestContextInterceptor" /> to this builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseRequestContext(this IInvokerBuilder builder) =>
        builder.Use(next => new RequestContextInterceptor(next));
}
