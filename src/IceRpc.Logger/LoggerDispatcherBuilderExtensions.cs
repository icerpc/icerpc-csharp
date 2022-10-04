// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Logger;
using Microsoft.Extensions.Logging;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the logger middleware to a <see cref="IDispatcherBuilder" />.
/// </summary>
public static class LoggerDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="LoggerMiddleware" /> to this dispatcher builder. This interceptor relies on the
    /// <see cref="ILogger{T}" /> service managed by the service provider.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IDispatcherBuilder UseLogger(this IDispatcherBuilder builder) =>
        builder.ServiceProvider.GetService(typeof(ILogger<LoggerMiddleware>)) is ILogger logger ?
        builder.Use(next => new LoggerMiddleware(next, logger)) :
        throw new InvalidOperationException(
            $"could not find service of type {nameof(ILogger<LoggerMiddleware>)} in service container");
}
