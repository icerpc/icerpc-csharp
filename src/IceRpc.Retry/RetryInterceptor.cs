// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Retry.Internal;
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

                do
                {
                    bool retryWithOtherReplica = false;

                    // At this point, response can be non-null and carry a failure for which we're retrying. If
                    // _next.InvokeAsync throws NoServerAddressException, we return this previous failure.
                    try
                    {
                        using IDisposable? scope = CreateRetryLogScope(attempt);

                        response = await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);

                        if (response.StatusCode == StatusCode.Success ||
                            (response.Protocol == Protocol.IceRpc && response.StatusCode != StatusCode.Unavailable) ||
                            (response.Protocol == Protocol.Ice && response.StatusCode != StatusCode.ServiceNotFound))
                        {
                            return response;
                        }
                        else
                        {
                            retryWithOtherReplica = true;
                        }
                    }
                    catch (NoServerAddressException ex)
                    {
                        // NoServerAddressException is always considered non-retryable; it typically occurs because we
                        // removed server addresses from serverAddressFeature.
                        return response ?? throw RethrowException(exception ?? ex);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception otherException)
                    {
                        response = null;
                        exception = otherException;
                    }

                    // We have an exception (possibly encoded in response) and the associated retry policy.
                    Debug.Assert(response is not null || exception is not null);

                    // Check if we can retry
                    tryAgain = false;
                    if (attempt < _options.MaxAttempts && decorator.IsResettable)
                    {
                        if (retryWithOtherReplica)
                        {
                            if (request.Features.Get<IServerAddressFeature>() is IServerAddressFeature serverAddressFeature &&
                                serverAddressFeature.ServerAddress is ServerAddress mainServerAddress)
                            {
                                // We don't want to retry with this server address
                                serverAddressFeature.RemoveServerAddress(mainServerAddress);

                                tryAgain = serverAddressFeature.ServerAddress is not null;
                            }
                            // else there is no replica to retry with
                        }
                        else if (request.Fields.ContainsKey(RequestFieldKey.Idempotent) ||
                                 !decorator.IsRead ||
                                 (exception is IceRpcException iceRpcException &&
                                    iceRpcException.IceRpcError == IceRpcError.ConnectionClosed))
                        {
                            tryAgain = true;
                        }

                        if (tryAgain)
                        {
                            attempt++;
                            decorator.Reset();
                        }
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

    private IDisposable? CreateRetryLogScope(int attempt) =>
        _logger != NullLogger.Instance && attempt > 1 ?
            _logger.RetryScope(attempt, _options.MaxAttempts) : null;
}
