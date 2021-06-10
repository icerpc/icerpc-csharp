// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Internal
{
    /// <summary>The implementation of <see cref="Interceptors.Retry"/>.</summary>
    internal sealed class RetryInvoker : IInvoker
    {
        private readonly int _bufferMaxSize;
        private int _bufferSize;

        private readonly ILogger _logger;
        private readonly int _maxAttempts;
        private readonly object _mutex = new();

        private readonly IInvoker _next;
        private readonly int _requestMaxSize;

        public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // If the request size is greater than _requestMaxSize or the size of the request would increase the
            // buffer size beyond _bufferMaxSize we release the request after it was sent to avoid holding too
            // much memory and we won't retry in case of a failure.

            int requestSize = request.PayloadSize;
            bool releaseRequestAfterSent = requestSize > _requestMaxSize || !IncBufferSize(requestSize);

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

                        retryPolicy = response.GetRetryPolicy(request.Proxy.Impl);
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
                    if (attempt == _maxAttempts ||
                        retryPolicy == RetryPolicy.NoRetry ||
                        (request.IsSent && releaseRequestAfterSent) ||
                        (retryPolicy == RetryPolicy.OtherReplica && (request.Connection?.IsIncoming ?? false)))
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
                            !request.Connection.IsIncoming &&
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
                                _maxAttempts,
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
                                _maxAttempts,
                                exception);
                        }

                        if (retryPolicy.Retryable == Retryable.AfterDelay && retryPolicy.Delay != TimeSpan.Zero)
                        {
                            // The delay task can be canceled either by the user code using the provided cancellation
                            // token or if the communicator is destroyed.
                            await Task.Delay(retryPolicy.Delay, cancel).ConfigureAwait(false);
                        }

                        if (request.Connection != null &&
                            !request.Connection.IsIncoming &&
                            (retryPolicy == RetryPolicy.OtherReplica || !request.Connection.IsActive))
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

        internal RetryInvoker(
            int maxAttempts,
            int bufferMaxSize,
            int requestMaxSize,
            ILoggerFactory? loggerFactory,
            IInvoker next)
        {
            _bufferMaxSize = bufferMaxSize;
            _maxAttempts = maxAttempts;
            _logger = (loggerFactory ?? Runtime.DefaultLoggerFactory).CreateLogger("IceRpc");
            _next = next;
            _requestMaxSize = requestMaxSize;
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
                if (size + _bufferSize < _bufferMaxSize)
                {
                    _bufferSize += size;
                    return true;
                }
            }
            return false;
        }
    }
}
