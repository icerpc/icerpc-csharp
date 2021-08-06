// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The retry interceptor is responsible for retrying requests when there is a retryable failure, it is
    /// typically configured before the <see cref="BinderInterceptor"/>.</summary>
    public class RetryInterceptor : IInvoker
    {
        /// <summary>Options class to configure <see cref="RetryInterceptor"/>.</summary>
        public sealed class Options
        {
            /// <summary>The maximum amount of memory in bytes used to hold all retryable requests. Once this limit is
            /// reached new requests are not retried and their memory is released after being sent. The default value is
            /// 100 MB</summary>
            public int BufferMaxSize
            {
                get => _bufferMaxSize;
                set
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
            public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

            /// <summary>The maximum number of attempts for retrying a request.</summary>
            public int MaxAttempts
            {
                get => _maxAttempts;
                set
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

            private int _bufferMaxSize = 1024 * 1024 * 100;
            private int _maxAttempts = 1;
            private int _requestMaxSize = 1024 * 1024;
        }

        private int _bufferSize;
        private readonly ILogger _logger;
        private readonly object _mutex = new();
        private readonly IInvoker _next;
        private readonly Options _options;

        /// <summary>Constructs a retry interceptor.</summary>
        /// <param name="next">The next invoker in the invocation pipeline.</param>
        /// <param name="options">The options to configure the retry interceptor.</param>
        /// <see cref="RetryPolicy"/>
        public RetryInterceptor(IInvoker next, Options options)
        {
            _next = next;
            _options = options;
            _logger = options.LoggerFactory.CreateLogger("IceRpc");
        }

        async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // If the request size is greater than _requestMaxSize or the size of the request would increase the
            // buffer size beyond _bufferMaxSize we release the request after it was sent to avoid holding too
            // much memory and we won't retry in case of a failure.

            int requestSize = request.PayloadSize;
            bool releaseRequestAfterSent = requestSize > _options.RequestMaxSize || !IncBufferSize(requestSize);

            int attempt = 1;
            IncomingResponse? response = null;
            Exception? exception = null;

            bool tryAgain;

            try
            {
                do
                {
                    RetryPolicy retryPolicy = RetryPolicy.NoRetry;
                    try
                    {
                        response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);

                        // TODO: release payload if releaseRequestAfterSent is true

                        if (response.ResultType == ResultType.Success)
                        {
                            return response;
                        }

                        retryPolicy = response.GetRetryPolicy(request.Proxy);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (NoEndpointException ex)
                    {
                        // NoEndpointException is always considered non-retryable; it typically occurs because we
                        // removed all remaining usable endpoints through request.ExcludedEndpoints.
                        return response ?? throw ExceptionUtil.Throw(exception ?? ex);
                    }
                    catch (Exception ex)
                    {
                        response = null;
                        exception = ex;
                        retryPolicy = request.RetryPolicy;
                    }

                    // Compute retry policy based on the exception or response retry policy, whether or not the
                    // connection is established or the request sent and idempotent
                    Debug.Assert(response != null || exception != null);

                    // Check if we can retry
                    if (attempt == _options.MaxAttempts ||
                        retryPolicy == RetryPolicy.NoRetry ||
                        (request.IsSent && releaseRequestAfterSent) ||
                        (retryPolicy == RetryPolicy.OtherReplica && (request.Connection?.IsServer ?? false)))
                    {
                        tryAgain = false;
                    }
                    else
                    {
                        // With the retry-policy OtherReplica we add the current endpoint to the list of excluded
                        // endpoints; this prevents the endpoints to be tried again during the current retry sequence.
                        // We use this ExcludedEndpoints list rather than simply removing the endpoint from the
                        // request.Endpoint/AltEndpoints because an interceptor down the line can change Endpoint /
                        // AltEndpoints, for example by re-resolving the original loc endpoint.
                        if (request.Connection != null &&
                            !request.Connection.IsServer &&
                            retryPolicy == RetryPolicy.OtherReplica)
                        {
                            request.ExcludedEndpoints =
                                request.ExcludedEndpoints.Append(request.Connection.RemoteEndpoint!);
                        }

                        tryAgain = true;
                        attempt++;

                        if (request.Connection != null)
                        {
                            using IDisposable? connectionScope = request.Connection.StartScope();
                            _logger.LogRetryRequestRetryableException(
                                request.Path,
                                request.Operation,
                                retryPolicy,
                                attempt,
                                _options.MaxAttempts,
                                exception);
                        }
                        else
                        {
                            // TODO: this is really a failure to establish a connection; other connection failure could
                            // leave request.Connection not null
                            _logger.LogRetryRequestConnectionException(
                                request.Path,
                                request.Operation,
                                retryPolicy,
                                attempt,
                                _options.MaxAttempts,
                                exception);
                        }

                        if (retryPolicy.Retryable == Retryable.AfterDelay && retryPolicy.Delay != TimeSpan.Zero)
                        {
                            await Task.Delay(retryPolicy.Delay, cancel).ConfigureAwait(false);
                        }

                        if (request.Connection != null &&
                            !request.Connection.IsServer &&
                            (retryPolicy == RetryPolicy.OtherReplica || request.Connection.State != ConnectionState.Active))
                        {
                            // Retry with a new connection
                            request.Connection = null;
                        }

                        // Reset relevant request properties before trying again.
                        request.IsSent = false;
                        request.RetryPolicy = RetryPolicy.NoRetry;
                    }
                }
                while (tryAgain);

                if (exception != null)
                {
                    // TODO this doesn't seems correct we need to log request exceptions even if there isn't
                    // a retry invoker
                    using IDisposable? connectionScope = request.Connection?.StartScope();
                    _logger.LogRequestException(request.Path, request.Operation, exception);
                }

                Debug.Assert(response != null || exception != null);
                Debug.Assert(response == null || response.ResultType == ResultType.Failure);
                return response ?? throw ExceptionUtil.Throw(exception!);
            }
            finally
            {
                if (!releaseRequestAfterSent)
                {
                    DecBufferSize(requestSize);
                }
                // TODO release the request memory if not already done after sent.
            }
        }

        private void DecBufferSize(int size)
        {
            lock (_mutex)
            {
                Debug.Assert(size <= _bufferSize);
                _bufferSize -= size;
            }
        }

        private bool IncBufferSize(int size)
        {
            lock (_mutex)
            {
                if (size + _bufferSize < _options.BufferMaxSize)
                {
                    _bufferSize += size;
                    return true;
                }
            }
            return false;
        }
    }
}
