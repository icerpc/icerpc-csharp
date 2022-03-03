// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Configure
{
    /// <summary>Options class to configure <see cref="RetryInterceptor"/>.</summary>
    public sealed record class RetryOptions
    {
        /// <summary>The maximum amount of memory in bytes used to hold all retryable requests. Once this limit is
        /// reached new requests are not retried and their memory is released after being sent. The default value is
        /// 100 MB</summary>
        public int BufferMaxSize
        {
            get => _bufferMaxSize;
            init
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(
                        $"Invalid value '{value}' for '{nameof(BufferMaxSize)}' it must be greater than 0.");
                }
                _bufferMaxSize = value;
            }
        }

        /// <summary>A logger factory used to create the retry interceptor logger.</summary>
        public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

        /// <summary>The maximum number of attempts for retrying a request.</summary>
        public int MaxAttempts
        {
            get => _maxAttempts;
            init
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(
                        $"Invalid value '{value}' for '{nameof(MaxAttempts)}', it must be greater than 0.");
                }
                _maxAttempts = value;
            }
        }

        /// <summary>The maximum payload size in bytes for a request to be retryable, requests with a bigger payload
        /// size are released after sent and cannot be retried. The default value is 1 MB.</summary>
        public int RequestMaxSize
        {
            get => _requestMaxSize;
            init
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(
                        $"Invalid value '{value}' for '{nameof(RequestMaxSize)}' it must be greater than 0.");
                }
                _requestMaxSize = value;
            }
        }

        private int _bufferMaxSize = 1024 * 1024 * 100;
        private int _maxAttempts = 1;
        private int _requestMaxSize = 1024 * 1024;
    }
}
