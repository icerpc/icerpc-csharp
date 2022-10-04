// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc.Deadline;

/// <summary>The deadline interceptor adds a deadline to requests without a deadline feature and encodes the deadline
/// field. When the deadline expires, the invocation is canceled and the interceptor throws
/// <see cref="TimeoutException" />. When used in conjunction with the retry interceptor it is important to install this
/// interceptor before the retry interceptor to ensure that the deadline is computed once per invocation and not once
/// per each retry.</summary>
public class DeadlineInterceptor : IInvoker
{
    private readonly bool _alwaysEnforceDeadline;
    private readonly IInvoker _next;
    private readonly TimeSpan _defaultTimeout;

    /// <summary>Constructs a deadline interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="defaultTimeout">The default timeout.</param>
    /// <param name="alwaysEnforceDeadline">When <see langword="true" /> and the request carries a deadline, the
    /// interceptor always creates a cancellation token source to enforce this deadline. When <see langword="false" />
    /// and the request carries a deadline, the interceptor creates a cancellation token source to enforce this deadline
    /// only when the invocation's cancellation token cannot be canceled. The default value is <see langword="false" />.
    /// </param>
    public DeadlineInterceptor(IInvoker next, TimeSpan defaultTimeout, bool alwaysEnforceDeadline)
    {
        _next = next;
        _alwaysEnforceDeadline = alwaysEnforceDeadline;
        _defaultTimeout = defaultTimeout;
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default)
    {
        TimeSpan? timeout = null;
        DateTime deadline = DateTime.MaxValue;

        if (request.Features.Get<IDeadlineFeature>() is IDeadlineFeature deadlineFeature)
        {
            deadline = deadlineFeature.Value;
            if (deadline != DateTime.MaxValue && (_alwaysEnforceDeadline || !cancellationToken.CanBeCanceled))
            {
                timeout = deadline - DateTime.UtcNow;
            }
        }
        else if (_defaultTimeout != Timeout.InfiniteTimeSpan)
        {
            timeout = _defaultTimeout;
            deadline = DateTime.UtcNow + timeout.Value;
        }

        if (timeout is not null && timeout.Value <= TimeSpan.Zero)
        {
            throw new TimeoutException("the request deadline has expired");
        }

        if (deadline != DateTime.MaxValue)
        {
            long deadlineValue = (long)(deadline - DateTime.UnixEpoch).TotalMilliseconds;
            request.Fields = request.Fields.With(
                RequestFieldKey.Deadline,
                (ref SliceEncoder encoder) => encoder.EncodeVarInt62(deadlineValue));
        }

        return timeout is null ? _next.InvokeAsync(request, cancellationToken) : PerformInvokeAsync(timeout.Value);

        async Task<IncomingResponse> PerformInvokeAsync(TimeSpan timeout)
        {
            using var timeoutTokenSource = new CancellationTokenSource(timeout);
            using CancellationTokenRegistration? _ = cancellationToken.CanBeCanceled ?
                cancellationToken.UnsafeRegister(cts => ((CancellationTokenSource)cts!).Cancel(), timeoutTokenSource) :
                null;

            try
            {
                return await _next.InvokeAsync(request, timeoutTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == timeoutTokenSource.Token)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("the request deadline has expired");
            }
        }
    }
}
