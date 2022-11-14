// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Retry.Internal;
using IceRpc.Slice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace IceRpc.Retry;

/// <summary>The retry interceptor is responsible for retrying requests when there is a retryable failure.</summary>
public class RetryInterceptor : IInvoker
{
    private readonly ILogger _logger;
    private readonly IInvoker _next;
    private readonly RetryOptions _options;

    /// <summary>Constructs a retry interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="options">The options to configure the retry interceptor.</param>
    /// <param name="logger">The logger.</param>
    /// <see cref="RetryPolicy" />
    public RetryInterceptor(IInvoker next, RetryOptions options, ILogger logger)
    {
        _next = next;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        if (request.PayloadContinuation is not null)
        {
            // This interceptor does not support retrying requests with a payload continuation.
            return await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var decorator = new ResettablePipeReaderDecorator(request.Payload, _options.MaxPayloadSize);
            request.Payload = decorator;

            try
            {
                int attempt = 1;
                IncomingResponse? response = null;
                Exception? exception = null;
                bool tryAgain;
                RetryPolicy retryPolicy = RetryPolicy.NoRetry;

                do
                {
                    // At this point, response can be non-null and carry a failure for which we're retrying.
                    try
                    {
                        using IDisposable? scope = CreateRetryLogScope(attempt, retryPolicy);
                        retryPolicy = RetryPolicy.NoRetry; // reset retry policy after logging

                        response = await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);

                        if (response.StatusCode == StatusCode.Success)
                        {
                            return response;
                        }
                        // else response carries a failure and we may want to retry

                        // Extracts the retry policy from the fields
                        retryPolicy = response.Fields.DecodeValue(
                            ResponseFieldKey.RetryPolicy,
                            (ref SliceDecoder decoder) => new RetryPolicy(ref decoder)) ?? RetryPolicy.NoRetry;
                    }
                    catch (OperationCanceledException)
                    {
                        // TODO: try other replica depending on who canceled the request?
                        throw;
                    }
                    catch (Exception newException)
                    {
                        exception = newException;

                        // An invocation on a closed connection is always safe to retry.
                        if ((exception is ConnectionException connectionException &&
                             connectionException.ErrorCode.IsClosedErrorCode()) ||
                            request.Fields.ContainsKey(RequestFieldKey.Idempotent) ||
                            !decorator.IsRead)
                        {
                            retryPolicy = RetryPolicy.Immediately;
                        }
                    }

                    // We have an exception (possibly encoded in response) and the associated retry policy.
                    Debug.Assert(response is not null || exception is not null);

                    // Check if we can retry
                    if (attempt < _options.MaxAttempts && retryPolicy != RetryPolicy.NoRetry && decorator.IsResettable)
                    {
                        if (request.Features.Get<IServerAddressFeature>()
                            is IServerAddressFeature serverAddressFeature && !serverAddressFeature.IsFromCache)
                        {
                            if (serverAddressFeature.ServerAddress is ServerAddress mainServerAddress &&
                                retryPolicy == RetryPolicy.OtherReplica)
                            {
                                // We don't want to retry with this server address
                                serverAddressFeature.RemoveServerAddress(mainServerAddress);
                            }

                            tryAgain = serverAddressFeature.ServerAddress is not null;
                        }
                        else
                        {
                            // If the server address feature was cached, the next attempt will refresh it.
                            // If there is no server address feature at all, server address(es) appear to be irrelevant,
                            // so we can retry even if request.ServiceAddress.ServerAddress is null.
                            tryAgain = true;
                        }
                    }
                    else
                    {
                        tryAgain = false;
                    }

                    if (tryAgain)
                    {
                        if (retryPolicy.Retryable == Retryable.AfterDelay && retryPolicy.Delay != TimeSpan.Zero)
                        {
                            await Task.Delay(retryPolicy.Delay, cancellationToken).ConfigureAwait(false);
                        }
                        attempt++;
                        decorator.Reset();
                    }
                }
                while (tryAgain);

                Debug.Assert(response is not null || exception is not null);
                Debug.Assert(response is null || response.StatusCode != StatusCode.Success);
                return response ?? throw RethrowException(exception!);
            }
            finally
            {
                // We want to leave request.Payload in a correct, usable state when we exit. Usually request.Payload
                // will get completed by the caller, and we want this Complete call to flow through to the decoratee.
                // If the payload is still readable (e.g. we received a non-retryable exception before reading anything
                // or just after a Reset), an upstream interceptor may want to attempt another call that reads this
                // payload and the now non-resettable decorator will provide the correct behavior. The decorator ensures
                // that calls to AdvanceTo on the decoratee always receive ever-increasing examined values even after
                // one or more Resets.
                decorator.IsResettable = false;
            }
        }
    }

    private static Exception RethrowException(Exception ex)
    {
        ExceptionDispatchInfo.Throw(ex);
        Debug.Assert(false);
        return ex;
    }

    private IDisposable? CreateRetryLogScope(int attempt, RetryPolicy retryPolicy) =>
        _logger != NullLogger.Instance && attempt > 1 ?
            _logger.RetryScope(attempt, _options.MaxAttempts, retryPolicy) : null;
}
