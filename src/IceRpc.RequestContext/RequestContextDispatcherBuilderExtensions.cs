// Copyright (c) ZeroC, Inc.

using IceRpc.RequestContext;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IDispatcherBuilder" /> to add the request context middleware.
/// </summary>
public static class RequestContextDispatcherBuilderExtensions
{
    /// <summary>Extension methods for <see cref="IDispatcherBuilder" />.</summary>
    /// <param name="builder">The builder being configured.</param>
    extension(IDispatcherBuilder builder)
    {
        /// <summary>Adds a <see cref="RequestContextMiddleware" /> to this dispatcher builder.</summary>
        /// <returns>The builder being configured.</returns>
        public IDispatcherBuilder UseRequestContext() =>
            builder.Use(next => new RequestContextMiddleware(next));
    }
}
