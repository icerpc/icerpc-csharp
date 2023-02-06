// Copyright (c) ZeroC, Inc.

using IceRpc.RequestContext;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the request context interceptor to an
/// <see cref="IInvokerBuilder" />.</summary>
public static class RequestContextInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="RequestContextInterceptor" /> to the builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseRequestContext(this IInvokerBuilder builder) =>
        builder.Use(next => new RequestContextInterceptor(next));
}
