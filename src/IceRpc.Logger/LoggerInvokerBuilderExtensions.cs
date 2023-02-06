// Copyright (c) ZeroC, Inc.

using IceRpc.Logger;
using Microsoft.Extensions.Logging;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the logger interceptor to an <see cref="IInvokerBuilder" />.
/// </summary>
public static class LoggerInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="LoggerInterceptor" /> to the builder. This interceptor relies on the
    /// <see cref="ILogger{T}" /> service managed by the service provider.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseLogger(this IInvokerBuilder builder) =>
        builder.ServiceProvider.GetService(typeof(ILogger<LoggerInterceptor>)) is ILogger logger ?
        builder.Use(next => new LoggerInterceptor(next, logger)) :
        throw new InvalidOperationException(
            $"Could not find service of type '{nameof(ILogger<LoggerInterceptor>)}' in the service container.");
}
