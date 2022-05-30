// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc.Deadline;

/// <summary>The deadline interceptor sets the invocation timeout and encodes the deadline field.</summary>
public class DeadlineInterceptor : IInvoker
{
    private readonly IInvoker _next;
    private readonly TimeSpan _timeout;

    /// <summary>Constructs a deadline interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="timeout">The default timeout for the request. This value can be overwritten by setting the
    /// <see cref="ITimeoutFeature"/> request feature.</param>
    public DeadlineInterceptor(IInvoker next, TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException($"{nameof(timeout)} must be greater than 0", nameof(timeout));
        }
        _next = next;
        _timeout = timeout;
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default)
    {
        TimeSpan timeout = Timeout.InfiniteTimeSpan;
        DateTime deadline = DateTime.MaxValue;
        if (request.Features.Get<IDeadlineFeature>() is IDeadlineFeature deadlineFeature)
        {
            if (deadlineFeature.Value != DateTime.MaxValue && !cancel.CanBeCanceled)
            {
                throw new InvalidOperationException(
                    "the request's cancellation token must be cancelable when a deadline is set");
            }
            deadline = deadlineFeature.Value;
        }
        else
        {
            timeout = request.Features.Get<ITimeoutFeature>()?.Value ?? _timeout;
            if (timeout != Timeout.InfiniteTimeSpan)
            {
                deadline = DateTime.UtcNow + timeout;
            }
        }

        if (deadline != DateTime.MaxValue)
        {
            long deadlineValue = (long)(deadline - DateTime.UnixEpoch).TotalMilliseconds;
            request.Fields = request.Fields.With(
                RequestFieldKey.Deadline,
                (ref SliceEncoder encoder) => encoder.EncodeVarInt62(deadlineValue));
        }

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return _next.InvokeAsync(request, cancel);
        }
        else
        {
            return PerformInvokeAsync(timeout);
        }

        async Task<IncomingResponse> PerformInvokeAsync(TimeSpan timeout)
        {
            using var timeoutTokenSource = new CancellationTokenSource(timeout);
            using CancellationTokenSource linkedTokenSource = cancel.CanBeCanceled ?
                CancellationTokenSource.CreateLinkedTokenSource(cancel, timeoutTokenSource.Token) :
                timeoutTokenSource;
            return await _next.InvokeAsync(request, linkedTokenSource.Token).ConfigureAwait(false);
        }
    }
}
