// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Logger;
using Microsoft.Extensions.Logging;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the logger interceptor to an <see cref="IInvokerBuilder"/>.
/// </summary>
public static class LoggerInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="LoggerInterceptor"/> to the builder. This interceptor relies on the
    /// <see cref="ILoggerFactory"/> service managed by the service provider.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseLogger(this IInvokerBuilder builder) =>
        builder.ServiceProvider.GetService(typeof(ILoggerFactory)) is ILoggerFactory loggerFactory ?
        builder.Use(next => new LoggerInterceptor(next, loggerFactory)) :
        throw new InvalidOperationException(
            $"could not find service of type {nameof(ILoggerFactory)} in service container");
}
