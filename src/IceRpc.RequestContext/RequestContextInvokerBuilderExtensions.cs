// Copyright (c) ZeroC, Inc.

using IceRpc.RequestContext;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IInvokerBuilder" /> to add the request context interceptor.
/// </summary>
public static class RequestContextInvokerBuilderExtensions
{
    /// <summary>Extension methods for <see cref="IInvokerBuilder" />.</summary>
    /// <param name="builder">The builder being configured.</param>
    extension(IInvokerBuilder builder)
    {
        /// <summary>Adds a <see cref="RequestContextInterceptor" /> to this builder.</summary>
        /// <returns>The builder being configured.</returns>
        public IInvokerBuilder UseRequestContext() =>
            builder.Use(next => new RequestContextInterceptor(next));
    }
}
