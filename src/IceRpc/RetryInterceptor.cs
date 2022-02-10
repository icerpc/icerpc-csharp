// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>The retry interceptor is responsible for retrying requests when there is a retryable failure, it is
    /// typically configured before the <see cref="BinderInterceptor"/>.</summary>
    public class RetryInterceptor : IInvoker
    {
        private readonly ILogger _logger;
        private readonly IInvoker _next;
        private readonly Configure.RetryOptions _options;

        /// <summary>Constructs a retry interceptor.</summary>
        /// <param name="next">The next invoker in the invocation pipeline.</param>
        /// <param name="options">The options to configure the retry interceptor.</param>
        /// <see cref="RetryPolicy"/>
        public RetryInterceptor(IInvoker next, Configure.RetryOptions options)
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

            // TODO: soon this won't work and the interceptor can't read the args size from the payload
            // int requestSize = request.Payload.GetByteCount();

            bool releaseRequestAfterSent = false; // requestSize > _options.RequestMaxSize;

            int attempt = 1;
            IncomingResponse? response = null;
            Exception? exception = null;

            bool tryAgain;

            var decorator = new ResettablePipeReaderDecorator(request.PayloadSource);
            await using var _ = decorator.ConfigureAwait(false);

            request.PayloadSource = decorator;

            try
            {
                do
                {
                    RetryPolicy retryPolicy = RetryPolicy.NoRetry;

                    // At this point, response can be non-null and carry a failure for which we're retrying. If
                    // _next.InvokeAsync throws NoEndpointException, we return this previous failure.
                    try
                    {
                        response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);

                        // TODO: release payload if releaseRequestAfterSent is true

                        if (response.ResultType == ResultType.Success)
                        {
                            return response;
                        }

                        retryPolicy = request.Features.Get<RetryPolicy>() ?? RetryPolicy.NoRetry;
                    }
                    catch (NoEndpointException ex)
                    {
                        // NoEndpointException is always considered non-retryable; it typically occurs because we
                        // removed all remaining usable endpoints through request.ExcludedEndpoints.
                        return response ?? throw ExceptionUtil.Throw(exception ?? ex);
                    }
                    catch (OperationCanceledException)
                    {
                        // TODO: try other replica in some cases?
                        throw;
                    }
                    catch (Exception ex)
                    {
                        response = null;
                        exception = ex;

                        // ConnectionClosedException is a graceful connection closure that is always safe to retry.
                        if (ex is ConnectionClosedException ||
                            (ex is TransportException &&
                                (request.Fields.ContainsKey((int)FieldKey.Idempotent) || !request.IsSent)))
                        {
                            retryPolicy = RetryPolicy.Immediately;
                        }
                    }

                    // Compute retry policy based on the exception or response retry policy, whether or not the
                    // connection is established or the request sent and idempotent
                    Debug.Assert(response != null || exception != null);

                    // Check if we can retry
                    if (attempt == _options.MaxAttempts ||
                        retryPolicy == RetryPolicy.NoRetry ||
                        !decorator.IsResettable ||
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
                            EndpointSelection? endpointSelection = request.Features.Get<EndpointSelection>();
                            if (endpointSelection != null)
                            {
                                endpointSelection.ExcludedEndpoints = endpointSelection.ExcludedEndpoints.Append(
                                    request.Connection.RemoteEndpoint!);
                            }
                        }

                        tryAgain = true;
                        attempt++;

                        _logger.LogRetryRequest(
                            request.Connection,
                            request.Proxy.Path,
                            request.Operation,
                            retryPolicy,
                            attempt,
                            _options.MaxAttempts,
                            exception);

                        if (retryPolicy.Retryable == Retryable.AfterDelay && retryPolicy.Delay != TimeSpan.Zero)
                        {
                            await Task.Delay(retryPolicy.Delay, cancel).ConfigureAwait(false);
                        }

                        if (request.Connection != null &&
                            !request.Connection.IsServer &&
                            (retryPolicy == RetryPolicy.OtherReplica ||
                             request.Connection.State != ConnectionState.Active))
                        {
                            // Retry with a new connection
                            request.Connection = null;
                        }

                        // Reset relevant request properties before trying again.
                        request.IsSent = false;
                        if (!request.Features.IsReadOnly)
                        {
                            request.Features.Set<RetryPolicy>(null);
                        }

                        decorator.Reset();
                    }
                }
                while (tryAgain);

                Debug.Assert(response != null || exception != null);
                Debug.Assert(response == null || response.ResultType == ResultType.Failure);
                return response ?? throw ExceptionUtil.Throw(exception!);
            }
            finally
            {
                // TODO release the request memory if not already done after sent.
            }
        }
    }

    internal static partial class RetryInterceptorLoggerExtensions
    {
        internal static void LogRetryRequest(
            this ILogger logger,
            Connection? connection,
            string path,
            string operation,
            RetryPolicy retryPolicy,
            int attempt,
            int maxAttempts,
            Exception? ex)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogRetryRequest(
                    connection?.NetworkConnectionInformation?.LocalEndpoint.ToString() ?? "undefined",
                    connection?.NetworkConnectionInformation?.RemoteEndpoint.ToString() ?? "undefined",
                    path,
                    operation,
                    retryPolicy,
                    attempt,
                    maxAttempts,
                    ex);
            }
        }

        [LoggerMessage(
            EventId = (int)RetryInterceptorEventIds.RetryRequest,
            EventName = nameof(RetryInterceptorEventIds.RetryRequest),
            Level = LogLevel.Information,
            Message = "retrying request because of retryable exception (LocalEndpoint={LocalEndpoint}, " +
                      "RemoteEndpoint={RemoteEndpoint}, Path={Path}, Operation={Operation}, " +
                      "RetryPolicy={RetryPolicy}, Attempt={Attempt}/{MaxAttempts})")]
        private static partial void LogRetryRequest(
            this ILogger logger,
            string localEndpoint,
            string remoteEndpoint,
            string path,
            string operation,
            RetryPolicy retryPolicy,
            int attempt,
            int maxAttempts,
            Exception? ex);
    }
}
