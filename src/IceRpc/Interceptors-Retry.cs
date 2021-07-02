// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;

namespace IceRpc
{
    public static partial class Interceptors
    {
        /// <summary>Creates a retry interceptor. The retry interceptor is reponsible for retrying requests when there
        /// is a retryable failure, it is typically configured before the
        /// <see cref="Binder(IConnectionProvider, bool)"/>.</summary>
        /// <param name="maxAttempts">The maximum number of attempts for retrying a request.</param>
        /// <param name="requestMaxSize">The maximum payload size in bytes for a request to be retryable, requests
        /// with a bigger payload size are released after sent and cannot be retried. The default value is 1 MB.
        /// </param>
        /// <param name="bufferMaxSize">The maximum amount of memory in bytes used to hold all retryable requests. Once
        /// this limit is reached new requests are not retried and their memory is released after being sent. The
        /// default value is 100 MB.</param>
        /// <param name="loggerFactory">A logger factory used to create the retry interceptor logger.</param>
        /// <returns>A new retry interceptor.</returns>
        /// <see cref="RetryPolicy"/>
        public static Func<IInvoker, IInvoker> Retry(
            int maxAttempts,
            int requestMaxSize = 1024 * 1024,
            int bufferMaxSize = 1024 * 1024 * 100,
            ILoggerFactory? loggerFactory = null)
        {
            // Creates a retry interceptor captured and shared by all invokers.
            var retryInterceptor = new RetryInterceptor(maxAttempts, requestMaxSize, bufferMaxSize, loggerFactory);
            return next => new InlineInvoker((request, cancel) => retryInterceptor.InvokeAsync(request, next, cancel));
        }
    }
}
