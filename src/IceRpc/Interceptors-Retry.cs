// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;

namespace IceRpc
{
    public static partial class Interceptors
    {
        /// <summary>An options class for configuring a <see cref="CustomRetry"/> interceptor.</summary>
        public sealed class RetryOptions
        {
            /// <summary>The maximum amount of memory in bytes that can be hold by all retryable requests, once this
            /// limit is reached new requests are not retriable and are released after sent. The default is 100 Mb.</summary>
            public int BufferMaxSize
            {
                get => _bufferMaxSize;
                set
                {
                    if (value < 1)
                    {
                        throw new ArgumentOutOfRangeException(
                            $"invalid value '{value}' for '{nameof(BufferMaxSize)}', it must be greater than 0.");
                    }
                    _bufferMaxSize = value;
                }
            }

            /// <summary>A logger factory used to create the retry interceptor logger.</summary>
            public ILoggerFactory? LoggerFactory { get; set; }

            /// <summary>The maximum number of attempts for retrying a request. Must be between 1 and 5, the default
            /// is 5 attempts.</summary>
            public int MaxAttempts
            {
                get => _maxAttempts;
                set
                {
                    if (value < 1 || value > 5)
                    {
                        throw new ArgumentOutOfRangeException(
                            $"Invalid value '{value}' for '{nameof(MaxAttempts)}', it must be between 1 and 5.");
                    }
                    _maxAttempts = value;
                }
            }

            /// <summary>The maximum payload size in bytes for a request to be retryable, requests with a bigger payload
            /// size are released after sent and cannot be retried. The default is 1 MB.</summary>
            public int RequestMaxSize
            {
                get => _requestMaxSize;
                set
                {
                    if (value < 1)
                    {
                        throw new ArgumentOutOfRangeException(
                            $"Invalid value '{value}' for '{nameof(RequestMaxSize)}' it must be greater than 0.");
                    }
                    _requestMaxSize = value;
                }
            }

            private int _maxAttempts = 5;
            private int _bufferMaxSize = 1024 * 1024 * 100;
            private int _requestMaxSize = 1024 * 1024;
        }

        /// <summary>A retry interceptor that use the default configuration. The retry interceptor is reponsible for
        /// retrying requests when there is a retryable failure, it is typically configured before the
        /// <see cref="Binder(IConnectionProvider, bool)"/>.</summary>
        /// <see cref="RetryPolicy"/>
        public static Func<IInvoker, IInvoker> Retry { get; } = CustomRetry(new RetryOptions());

        /// <summary>Creates a retry interceptor. The retry interceptor is reponsible for retrying requests when there
        /// is a retryable failure, it is typically configured before the
        /// <see cref="Binder(IConnectionProvider, bool)"/>.</summary>
        /// <param name="retryOptions">The retry options to configure the retry interceptor.</param>
        /// <returns>A new retry interceptor.</returns>
        public static Func<IInvoker, IInvoker> CustomRetry(RetryOptions retryOptions) =>
                next => new RetryInvoker(retryOptions, next);
    }
}
