// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Features;
using IceRpc.Retry.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace IceRpc.Retry;

/// <summary>The retry interceptor is responsible for retrying failed requests when the failure condition can be
/// retried.</summary>
/// <remarks>A failed request can be retried if:
/// <list type="bullet">
/// <item><description><see cref="RetryOptions.MaxAttempts" /> is not reached.</description></item>
/// <item><description><see cref="OutgoingFrame.Payload" /> can be read again.</description></item>
/// <item><description>The failure condition can be retried.</description></item>
/// </list>
/// <para>In order to be able to read again the request's payload, the retry interceptor decorates the payload
/// with <see cref="ResettablePipeReaderDecorator" />. The decorator can be reset as long as the buffered data doesn't
/// exceed <see cref="RetryOptions.MaxPayloadSize" />.</para>
/// <para>The request can be retried under the following failure conditions:</para>
/// <list type="bullet">
/// <item><description>The status code carried by the response is <see cref="StatusCode.Unavailable"
/// />.</description></item>
/// <item><description>The status code carried by the response is <see cref="StatusCode.NotFound" /> and the
/// protocol is ice.</description></item>
/// <item><description>The request failed with an <see cref="IceRpcException" /> with one of the following error:
/// <list type="bullet">
/// <item><description>The error code is <see cref="IceRpcError.InvocationCanceled" />.</description></item>
/// <item><description>The error code is <see cref="IceRpcError.ConnectionAborted" /> or <see
/// cref="IceRpcError.TruncatedData" /> and the request has the <see cref="RequestFieldKey.Idempotent" />
/// field.</description></item>
/// </list></description></item>
/// </list>
/// <para>If the status code carried by the response is <see cref="StatusCode.Unavailable" /> or <see
/// cref="StatusCode.NotFound" /> (with the ice protocol), the address of the server is removed from the set of
/// server addresses to retry on. This ensures the request won't be retried on the unavailable server.</para></remarks>
/// <seealso cref="RetryPipelineExtensions"/>
/// <seealso cref="RetryInvokerBuilderExtensions"/>
public class RetryInterceptor : IInvoker
{
    private readonly ILogger _logger;
    private readonly int _maxAttempts;
    private readonly int _maxPayloadSize;
    private readonly IInvoker _next;

    /// <summary>Constructs a retry interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="options">The options to configure the retry interceptor.</param>
    /// <param name="logger">The logger.</param>
    public RetryInterceptor(IInvoker next, RetryOptions options, ILogger logger)
    {
        _next = next;
        _maxAttempts = options.MaxAttempts;
        _maxPayloadSize = options.MaxPayloadSize;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        // This interceptor does not support retrying requests with a payload continuation.
        if (request.PayloadContinuation is not null || _maxAttempts == 1)
        {
            return await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var decorator = new ResettablePipeReaderDecorator(request.Payload, _maxPayloadSize);
            request.Payload = decorator;

            try
            {
                int attempt = 1;
                IncomingResponse? response = null;
                IceRpcException? exception = null;
                bool tryAgain;

                do
                {
                    bool retryWithOtherReplica = false;

                    try
                    {
                        using IDisposable? scope = CreateRetryLogScope(attempt);

                        response = await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);

                        if (response.StatusCode == StatusCode.Unavailable ||
                            (response.Protocol == Protocol.Ice && response.StatusCode == StatusCode.NotFound))
                        {
                            retryWithOtherReplica = true;
                        }
                        else
                        {
                            return response;
                        }
                    }
                    catch (IceRpcException iceRpcException) when (
                        iceRpcException.IceRpcError == IceRpcError.NoConnection)
                    {
                        // NoConnection is always considered non-retryable; it typically occurs because we removed all
                        // the server addresses from serverAddressFeature. Unlike other non-retryable exceptions, we
                        // privilege returning the previous response (if any).
                        return response ?? throw RethrowException(exception ?? iceRpcException);
                    }
                    catch (IceRpcException iceRpcException)
                    {
                        response = null;
                        exception = iceRpcException;
                    }

                    Debug.Assert(retryWithOtherReplica || exception is not null);

                    // Check if we can retry
                    tryAgain = false;

                    // The decorator is non-resettable when we've reached the last attempt (see below) or the decorator
                    // caught an exception that prevents resetting, like a ReadAsync exception.
                    if (decorator.IsResettable)
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
                        else
                        {
                            Debug.Assert(exception is not null);
                            // It's always safe to retry InvocationCanceled because it's only raised before the request
                            // is sent to the peer. For idempotent requests we also retry on ConnectionAborted and
                            // TruncatedData.
                            tryAgain = exception.IceRpcError switch
                            {
                                IceRpcError.InvocationCanceled => true,
                                IceRpcError.ConnectionAborted or IceRpcError.TruncatedData
                                    when request.Fields.ContainsKey(RequestFieldKey.Idempotent) => true,
                                _ => false
                            };
                        }

                        if (tryAgain)
                        {
                            attempt++;
                            decorator.Reset();

                            // If this attempt is the last attempt, we make the decorator non-resettable to release the
                            // memory for the request payload as soon as possible.
                            if (attempt == _maxAttempts)
                            {
                               decorator.IsResettable = false;
                            }
                        }
                    }
                }
                while (tryAgain);

                Debug.Assert(response is not null || exception is not null);
                Debug.Assert(response is null || response.StatusCode != StatusCode.Ok);
                return response ?? throw RethrowException(exception!);
            }
            finally
            {
                // We want to leave request.Payload in a correct, usable state when we exit. Usually request.Payload
                // will get completed by the caller, and we want this Complete call to flow through to the decoratee. If
                // the payload is still readable (e.g. we received a non-retryable exception before reading anything or
                // just after a Reset), an upstream interceptor may want to attempt another call that reads this payload
                // and the now non-resettable decorator will provide the correct behavior. The decorator ensures that
                // calls to AdvanceTo on the decoratee always receive ever-increasing examined values even after one or
                // more Resets.
                decorator.IsResettable = false;
            }
        }
    }

    private static Exception RethrowException(Exception exception)
    {
        ExceptionDispatchInfo.Throw(exception);
        Debug.Assert(false);
        return exception;
    }

    private IDisposable? CreateRetryLogScope(int attempt) =>
        _logger != NullLogger.Instance && attempt > 1 ? _logger.RetryScope(attempt, _maxAttempts) : null;
}
