// Copyright (c) ZeroC, Inc. All rights reserved.

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
        if (request.PayloadStream is not null)
        {
            // This interceptor does not support retrying requests with a payload stream.
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
                        return response ?? throw ExceptionUtil.Throw(exception ?? ex);
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
                            !request.IsSent)
                        {
                            retryPolicy = RetryPolicy.Immediately;
                        }
                    }

                    // We have an exception (possibly encoded in response) and the associated retry policy.
                    Debug.Assert(response != null || exception != null);

                    // Check if we can retry
                    if (attempt < _options.MaxAttempts && retryPolicy != RetryPolicy.NoRetry && decorator.IsResettable)
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

                        request.IsSent = false;
                        decorator.Reset();
                    }
                    else
                    {
                        tryAgain = false;
                    }
                }
                while (tryAgain);

                Debug.Assert(response != null || exception != null);
                Debug.Assert(response == null || response.ResultType != ResultType.Success);
                return response ?? throw ExceptionUtil.Throw(exception!);
            }
            finally
            {
                decorator.IsResettable = false;
            }
        }
    }
}
