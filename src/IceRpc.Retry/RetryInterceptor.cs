﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Retry.Internal;
using IceRpc.Slice;
using Microsoft.Extensions.Logging;
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
    /// <see cref="RetryPolicy"/>
    public RetryInterceptor(IInvoker next, RetryOptions options)
    {
        _next = next;
        _options = options;
        _logger = options.LoggerFactory.CreateLogger("IceRpc");
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        IEndpointFeature? endpointFeature = request.Features.Get<IEndpointFeature>();
        if (endpointFeature == null)
        {
            endpointFeature = new EndpointFeature(request.Proxy);
            request.Features = request.Features.With(endpointFeature);
        }

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

internal static partial class RetryInterceptorLoggerExtensions
{
    internal static void LogRetryRequest(
        this ILogger logger,
        IConnection? connection,
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
                connection?.NetworkConnectionInformation?.LocalEndPoint.ToString() ?? "undefined",
                connection?.NetworkConnectionInformation?.RemoteEndPoint.ToString() ?? "undefined",
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
        Message = "retrying request because of retryable exception (LocalEndPoint={LocalEndPoint}, " +
                  "RemoteEndPoint={RemoteEndPoint}, Path={Path}, Operation={Operation}, " +
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
