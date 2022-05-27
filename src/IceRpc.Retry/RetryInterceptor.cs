// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Retry.Internal;
using IceRpc.Slice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace IceRpc.Retry;

/// <summary>The retry interceptor is responsible for retrying requests when there is a retryable failure, it is
/// typically configured before the binder interceptor.</summary>
public class RetryInterceptor : IInvoker
{
    private readonly ILogger _logger;
    private readonly IInvoker _next;
    private readonly RetryOptions _options;

    /// <summary>Constructs a retry interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="options">The options to configure the retry interceptor.</param>
    /// <param name="loggerFactory">The logger factory used to create loggers to log retries.</param>
    /// <see cref="RetryPolicy"/>
    public RetryInterceptor(IInvoker next, RetryOptions options, ILoggerFactory? loggerFactory = null)
    {
        _next = next;
        _options = options;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("IceRpc");
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        if (request.PayloadStream != null)
        {
            // If the request include a payload stream it cannot be retried
            return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
        else
        {
            IEndpointFeature? endpointFeature = request.Features.Get<IEndpointFeature>();
            if (endpointFeature == null)
            {
                endpointFeature = new EndpointFeature(request.Proxy);
                request.Features = request.Features.With(endpointFeature);
            }

            bool releaseRequestAfterSent = false;
            int attempt = 1;
            IncomingResponse? response = null;
            Exception? exception = null;

            bool tryAgain;

            var decorator = new ResettablePipeReaderDecorator(request.Payload);
            await using var _ = decorator.ConfigureAwait(false);

            request.Payload = decorator;

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
                        // else response carries a failure and we may want to retry

                        // Extracts the retry policy from the fields
                        retryPolicy = response.Fields.DecodeValue(
                            ResponseFieldKey.RetryPolicy,
                            (ref SliceDecoder decoder) => new RetryPolicy(ref decoder)) ?? RetryPolicy.NoRetry;
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
                            request.Fields.ContainsKey(RequestFieldKey.Idempotent) ||
                            !request.IsSent)
                        {
                            retryPolicy = RetryPolicy.Immediately;
                        }
                    }
                    finally
                    {
                        // If the request size is greater than _requestMaxSize we release the request after it was sent to avoid
                        // holding too much memory and we won't retry in case of a failure.
                        releaseRequestAfterSent = decorator.Consumed > _options.RequestMaxSize;
                    }

                    // Compute retry policy based on the exception or response retry policy, whether or not the
                    // connection is established or the request sent and idempotent
                    Debug.Assert(response != null || exception != null);

                    // Check if we can retry
                    if (attempt == _options.MaxAttempts ||
                        retryPolicy == RetryPolicy.NoRetry ||
                        !decorator.IsResettable ||
                        (request.IsSent && releaseRequestAfterSent))
                    {
                        tryAgain = false;
                    }
                    else
                    {
                        if (request.Connection is IClientConnection clientConnection &&
                             retryPolicy == RetryPolicy.OtherReplica)
                        {
                            endpointFeature.RemoveEndpoint(clientConnection.RemoteEndpoint);
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
                            (retryPolicy == RetryPolicy.OtherReplica || !request.Connection.IsInvocable))
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
                Debug.Assert(response == null || response.ResultType != ResultType.Success);
                return response ?? throw ExceptionUtil.Throw(exception!);
            }
            finally
            {
                // TODO release the request memory if not already done after sent.
            }
        }
    }
}
