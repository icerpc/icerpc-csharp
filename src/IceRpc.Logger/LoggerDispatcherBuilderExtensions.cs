// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Logger;
using Microsoft.Extensions.Logging;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add a logger middleware to a <see cref="IDispatcherBuilder"/>.
/// </summary>
public static class LoggerDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="LoggerMiddleware"/> to this dispatcher builder.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IDispatcherBuilder UseLogger(this IDispatcherBuilder builder) =>
        builder.ServiceProvider.GetService(typeof(ILoggerFactory)) is ILoggerFactory loggerFactory ?
        builder.Use(next => new LoggerMiddleware(next, loggerFactory)) :
        throw new InvalidOperationException(
            $"could not find service of type {nameof(ILoggerFactory)} in service container");
}
