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
    /// <param name="loggerFactory">The logger factory used to create loggers to log retries.</param>
    /// <see cref="RetryPolicy"/>
    public RetryInterceptor(IInvoker next, RetryOptions options, ILoggerFactory? loggerFactory = null)
    {
        _next = next;
        _options = options;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger(GetType().FullName!);
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
                    // _next.InvokeAsync throws NoServerAddressException, we return this previous failure.
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
                    catch (NoServerAddressException ex)
                    {
                        // NoServerAddressException is always considered non-retryable; it typically occurs because we
                        // removed server addresses from endpoinFeature.
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
                        tryAgain = true;
                        attempt++;

                        _logger.LogRetryRequest(
                            request.ServiceAddress.Path,
                            request.Operation,
                            retryPolicy,
                            attempt,
                            _options.MaxAttempts,
                            exception);

                        if (retryPolicy.Retryable == Retryable.AfterDelay && retryPolicy.Delay != TimeSpan.Zero)
                        {
                            await Task.Delay(retryPolicy.Delay, cancel).ConfigureAwait(false);
                        }

                        if (request.Features.Get<IServerAddressFeature>() is IServerAddressFeature serverAddressFeature &&
                            serverAddressFeature.ServerAddress is ServerAddress mainServerAddress &&
                            retryPolicy == RetryPolicy.OtherReplica)
                        {
                            // We don't want to retry with this server address
                            serverAddressFeature.RemoveEndpoint(mainServerAddress);
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
    }

    private static Exception RethrowException(Exception ex)
    {
        ExceptionDispatchInfo.Throw(ex);
        Debug.Assert(false);
        return ex;
    }
}
