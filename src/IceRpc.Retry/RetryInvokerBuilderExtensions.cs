// Copyright (c) ZeroC, Inc.

using IceRpc.Retry;
using Microsoft.Extensions.Logging;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to add the retry interceptor to a <see cref="IInvokerBuilder" />.
/// </summary>
public static class RetryInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="RetryInterceptor" /> that uses the default <see cref="RetryOptions" />.</summary>
    /// <param name="builder">The pipeline being configured.</param>
    /// <returns>The pipeline being configured.</returns>
    public static IInvokerBuilder UseRetry(this IInvokerBuilder builder) =>
        builder.UseRetry(new RetryOptions());

    /// <summary>Adds a <see cref="RetryInterceptor" /> to the builder. This interceptor relies on the
    /// <see cref="ILoggerFactory" /> service managed by the service provider.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="options">The options to configure the <see cref="RetryInterceptor" />.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseRetry(this IInvokerBuilder builder, RetryOptions options) =>
        builder.ServiceProvider.GetService(typeof(ILogger<RetryInterceptor>)) is ILogger logger ?
        builder.Use(next => new RetryInterceptor(next, options, logger)) :
        throw new InvalidOperationException(
            $"Could not find service of type '{nameof(ILogger<RetryInterceptor>)}' in the service container.");
}
