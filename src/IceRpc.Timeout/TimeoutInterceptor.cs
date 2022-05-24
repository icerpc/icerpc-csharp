// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Deadline;
using IceRpc.Features;

namespace IceRpc.Timeout;

/// <summary>The timeout interceptor adds and enforces a timeout for requests with no deadline set.</summary>
/// <remarks>This interceptor sets the request's deadline. As a result, if you insert more than one timeout
/// interceptor in an invocation pipeline, only the first one has any effect.</remarks>
public class TimeoutInterceptor : IInvoker
{
    private readonly IInvoker _next;
    private readonly TimeSpan _timeout;

    /// <summary>Constructs a timeout interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="timeout">The timeout for the invocation.</param>
    public TimeoutInterceptor(IInvoker next, TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero &&
            timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException($"{nameof(timeout)} must be greater than 0", nameof(timeout));
        }

        _next = next;
        _timeout = timeout;
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        // If the deadline feature is set, we don't do anything
        if (request.Features.Get<IDeadlineFeature>() is IDeadlineFeature deadlineFeature &&
            deadlineFeature.Value != DateTime.MaxValue)
        {
            return _next.InvokeAsync(request, cancel);
        }
        else
        {
            TimeSpan timeout = request.Features.Get<ITimeoutFeature>()?.Value ?? _timeout;
            if (timeout == System.Threading.Timeout.InfiniteTimeSpan)
            {
                return _next.InvokeAsync(request, cancel);
            }
            else
            {
                return PerformInvokeAsync(timeout);
            }
        }

        async Task<IncomingResponse> PerformInvokeAsync(TimeSpan timeout)
        {
            using var timeoutTokenSource = new CancellationTokenSource(timeout);
            using CancellationTokenSource linkedTokenSource = cancel.CanBeCanceled ?
                CancellationTokenSource.CreateLinkedTokenSource(cancel, timeoutTokenSource.Token) :
                timeoutTokenSource;

            // We compute the deadline immediately
            request.Features = request.Features.With<IDeadlineFeature>(
                new DeadlineFeature
                {
                    Value = DateTime.UtcNow + timeout
                });

            return await _next.InvokeAsync(request, linkedTokenSource.Token).ConfigureAwait(false);
        }
    }
}
