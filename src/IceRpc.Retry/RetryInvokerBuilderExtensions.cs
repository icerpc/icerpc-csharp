// Copyright (c) ZeroC, Inc.

using IceRpc.Retry;
using Microsoft.Extensions.Logging;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides extension methods for <see cref="IInvokerBuilder" /> to add the retry interceptor.</summary>
public static class RetryInvokerBuilderExtensions
{
    /// <summary>Extension methods for <see cref="IInvokerBuilder" />.</summary>
    /// <param name="builder">The pipeline being configured.</param>
    extension(IInvokerBuilder builder)
    {
        /// <summary>Adds a <see cref="RetryInterceptor" /> that uses the default <see cref="RetryOptions" />.</summary>
        /// <returns>The pipeline being configured.</returns>
        public IInvokerBuilder UseRetry() =>
            builder.UseRetry(new RetryOptions());

        /// <summary>Adds a <see cref="RetryInterceptor" /> to the builder. This interceptor relies on the
        /// <see cref="ILogger{T}" /> service managed by the service provider.</summary>
        /// <param name="options">The options to configure the <see cref="RetryInterceptor" />.</param>
        /// <returns>The builder being configured.</returns>
        public IInvokerBuilder UseRetry(RetryOptions options) =>
            builder.ServiceProvider.GetService(typeof(ILogger<RetryInterceptor>)) is ILogger logger ?
            builder.Use(next => new RetryInterceptor(next, options, logger)) :
            throw new InvalidOperationException(
                $"Could not find service of type '{nameof(ILogger<RetryInterceptor>)}' in the service container.");
    }
}
