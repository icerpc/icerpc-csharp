// Copyright (c) ZeroC, Inc.

using IceRpc.Retry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc;

/// <summary>Provides extension methods for <see cref="Pipeline" /> to add the retry interceptor.</summary>
public static class RetryPipelineExtensions
{
    /// <summary>Extension methods for <see cref="Pipeline" />.</summary>
    /// <param name="pipeline">The pipeline being configured.</param>
    extension(Pipeline pipeline)
    {
        /// <summary>Adds a <see cref="RetryInterceptor" /> that uses the default <see cref="RetryOptions" /> to the
        /// pipeline.</summary>
        /// <returns>The pipeline being configured.</returns>
        /// <example>
        /// The following code adds the retry interceptor using the default <see cref="RetryOptions"/> to
        /// the invocation pipeline.
        /// <code source="../../docfx/examples/IceRpc.Retry.Examples/RetryInterceptorExamples.cs"
        /// region="UseRetry" lang="csharp" />
        /// </example>
        /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/main/examples/Retry"/>
        public Pipeline UseRetry() =>
            pipeline.UseRetry(new RetryOptions());

        /// <summary>Adds a <see cref="RetryInterceptor" /> to the pipeline.</summary>
        /// <param name="options">The options to configure the <see cref="RetryInterceptor" />.</param>
        /// <returns>The pipeline being configured.</returns>
        /// <example>
        /// The following code adds the retry interceptor using the provided <see cref="RetryOptions"/> to
        /// the invocation pipeline.
        /// <code source="../../docfx/examples/IceRpc.Retry.Examples/RetryInterceptorExamples.cs"
        /// region="UseRetryWithOptions" lang="csharp" />
        /// </example>
        /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/main/examples/Retry"/>
        public Pipeline UseRetry(RetryOptions options) =>
            pipeline.UseRetry(options, NullLoggerFactory.Instance);

        /// <summary>Adds a <see cref="RetryInterceptor" /> to the pipeline.</summary>
        /// <param name="options">The options to configure the <see cref="RetryInterceptor" />.</param>
        /// <param name="loggerFactory">The logger factory used to create a <see cref="ILogger{TCategoryName}" />
        /// for <see cref="RetryInterceptor" />.</param>
        /// <returns>The pipeline being configured.</returns>
        /// <example>
        /// The following code adds the retry interceptor using the provided <see cref="ILoggerFactory"/> and
        /// <see cref="RetryOptions"/> to the invocation pipeline.
        /// <code source="../../docfx/examples/IceRpc.Retry.Examples/RetryInterceptorExamples.cs"
        /// region="UseRetryWithLoggerFactory" lang="csharp" />
        /// </example>
        /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/main/examples/Retry"/>
        public Pipeline UseRetry(RetryOptions options, ILoggerFactory loggerFactory) =>
            pipeline.Use(next => new RetryInterceptor(next, options, loggerFactory.CreateLogger<RetryInterceptor>()));
    }
}
