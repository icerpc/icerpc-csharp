// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;

namespace IceRpc
{
    public static partial class Interceptor
    {
        /// <summary>Creates a retry interceptor.</summary>
        /// <param name="maxAttempts">The maximum number of attempts for an invocation. Must be between 1 and 5.</param>
        /// <param name="bufferMaxSize">TBD</param>
        /// <param name="requestMaxSize">TBD</param>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <returns>A new retry interceptor.</returns>
        public static Func<IInvoker, IInvoker> Retry(
            int maxAttempts,
            int bufferMaxSize = 1024 * 1024 * 100,
            int requestMaxSize = 1024 * 1024,
            ILoggerFactory? loggerFactory = null) =>
                next => new RetryInvoker(maxAttempts, bufferMaxSize, requestMaxSize, loggerFactory, next);
    }
}
