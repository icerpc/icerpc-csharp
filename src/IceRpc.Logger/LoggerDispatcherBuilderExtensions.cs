// Copyright (c) ZeroC, Inc.

using IceRpc.Logger;
using Microsoft.Extensions.Logging;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides an extension method for <see cref="IDispatcherBuilder" /> to add the logger middleware.</summary>
public static class LoggerDispatcherBuilderExtensions
{
    /// <summary>Extension methods for <see cref="IDispatcherBuilder" />.</summary>
    /// <param name="builder">The builder being configured.</param>
    extension(IDispatcherBuilder builder)
    {
        /// <summary>Adds a <see cref="LoggerMiddleware" /> to this dispatcher builder. This interceptor relies on the
        /// <see cref="ILogger{T}" /> service managed by the service provider.</summary>
        /// <returns>The builder being configured.</returns>
        public IDispatcherBuilder UseLogger() =>
            builder.ServiceProvider.GetService(typeof(ILogger<LoggerMiddleware>)) is ILogger logger ?
            builder.Use(next => new LoggerMiddleware(next, logger)) :
            throw new InvalidOperationException(
                $"Could not find service of type '{nameof(ILogger<LoggerMiddleware>)}' in the service container.");
    }
}
