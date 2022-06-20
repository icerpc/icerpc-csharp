// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc.Deadline;

/// <summary>The deadline interceptor adds a deadline to requests without a deadline feature, encodes the deadline field
/// and enforces the deadline.</summary>
public class DeadlineInterceptor : IInvoker
{
    private readonly bool _alwaysEnforceDeadline;
    private readonly IInvoker _next;
    private readonly TimeSpan _defaultTimeout;

    /// <summary>Constructs a deadline interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="options">The deadline interceptor options.</param>
    public DeadlineInterceptor(IInvoker next, DeadlineInterceptorOptions options)
    {
        _next = next;
        _alwaysEnforceDeadline = options.AlwaysEnforceDeadline;
        _defaultTimeout = options.DefaultTimeout;
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default)
    {
        TimeSpan timeout = Timeout.InfiniteTimeSpan;
        DateTime deadline = DateTime.MaxValue;
        bool enforceDeadline = false;

        if (request.Features.Get<IDeadlineFeature>() is IDeadlineFeature deadlineFeature)
        {
            deadline = deadlineFeature.Value;
            if (deadline != DateTime.MaxValue)
            {
                enforceDeadline = _alwaysEnforceDeadline || !cancel.CanBeCanceled;
                if (enforceDeadline)
                {
                    timeout = deadline - DateTime.UtcNow;

                    if (timeout < TimeSpan.Zero)
                    {
                        throw new TimeoutException("the request deadline has expired");
                    }
                }
            }
        }
        else if (_defaultTimeout != Timeout.InfiniteTimeSpan)
        {
            timeout = _defaultTimeout;
            deadline = DateTime.UtcNow + timeout;
            enforceDeadline = true;
        }

        if (deadline != DateTime.MaxValue)
        {
            long deadlineValue = (long)(deadline - DateTime.UnixEpoch).TotalMilliseconds;
            request.Fields = request.Fields.With(
                RequestFieldKey.Deadline,
                (ref SliceEncoder encoder) => encoder.EncodeVarInt62(deadlineValue));
        }

        return enforceDeadline ? PerformInvokeAsync(timeout) : _next.InvokeAsync(request, cancel);

        async Task<IncomingResponse> PerformInvokeAsync(TimeSpan timeout)
        {
            using var timeoutTokenSource = new CancellationTokenSource(timeout);
            using CancellationTokenSource linkedTokenSource = cancel.CanBeCanceled ?
                CancellationTokenSource.CreateLinkedTokenSource(cancel, timeoutTokenSource.Token) :
                timeoutTokenSource;

            try
            {
                return await _next.InvokeAsync(request, linkedTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == linkedTokenSource.Token)
            {
                cancel.ThrowIfCancellationRequested();
                throw new TimeoutException("the request deadline has expired");
            }
        }
    }
}
