// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Retry.Internal;
using IceRpc.Slice;
using IceRpc.Transports;
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
    /// <param name="loggerFactory">The logger factory used to create loggers to log retries.</param>
    /// <see cref="RetryPolicy"/>
    public RetryInterceptor(IInvoker next, RetryOptions options, ILoggerFactory? loggerFactory = null)
    {
        _next = next;
        _options = options;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("IceRpc.Retry");
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        if (request.PayloadStream is not null)
        {
            // This interceptor does not support retrying requests with a payload stream.
            return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
        else
        {
            IEndpointFeature? endpointFeature = request.Features.Get<IEndpointFeature>();
            if (endpointFeature is null)
            {
                endpointFeature = new EndpointFeature(request.Proxy);
                request.Features = request.Features.With(endpointFeature);
            }

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
                    RetryPolicy retryPolicy = RetryPolicy.NoRetry;

                    // At this point, response can be non-null and carry a failure for which we're retrying. If
                    // _next.InvokeAsync throws NoEndpointException, we return this previous failure.
                    try
                    {
                        response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);

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
                        // removed endpoints from endpoinFeature.
                        return response ?? throw RethrowException(exception ?? ex);
                    }
                    catch (OperationCanceledException)
                    {
                        // TODO: try other replica depending on who canceled the request?
                        throw;
                    }
                    catch (Exception ex)
                    {
                        response = null;
                        exception = ex;

                        // ConnectionClosedException is a graceful connection closure that is always safe to retry.
                        if (ex is ConnectionClosedException ||
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
                        if (endpointFeature.Connection is IClientConnection clientConnection &&
                             retryPolicy == RetryPolicy.OtherReplica)
                        {
                            endpointFeature.RemoveEndpoint(clientConnection.RemoteEndpoint);
                        }

                        tryAgain = true;
                        attempt++;

                        _logger.LogRetryRequest(
                            endpointFeature.Connection,
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

                        // Clear connection is the retry policy is other replica or the current connection is unusable.
                        if (endpointFeature is IConnection connection &&
                            (retryPolicy == RetryPolicy.OtherReplica ||
                                (!connection.IsResumable && IsDeadConnectionException(exception))))
                        {
                            endpointFeature.Connection = null;
                        }

                        decorator.Reset();
                    }
                    else
                    {
                        tryAgain = false;
                    }
                }
                while (tryAgain);

                Debug.Assert(response is not null || exception is not null);
                Debug.Assert(response is null || response.ResultType != ResultType.Success);
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

        static bool IsDeadConnectionException(Exception? exception) =>
            exception is
                ConnectionAbortedException or
                ConnectionClosedException or
                ConnectionLostException or
                ConnectFailedException or
                TimeoutException;
    }

    private static Exception RethrowException(Exception ex)
    {
        ExceptionDispatchInfo.Throw(ex);
        Debug.Assert(false);
        return ex;
    }
}
