// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;

namespace IceRpc.Deadline;

/// <summary>Represents an interceptor that sets deadlines on requests without deadlines, and enforces these deadlines.
/// </summary>
/// <remarks>When a request doesn't carry an <see cref="IDeadlineFeature"/> feature, this interceptor computes a
/// deadline using its configured default timeout; otherwise, it uses the request's existing deadline feature. It then
/// encodes the deadline value as a <see cref="RequestFieldKey.Deadline" /> field and makes the invocation throw a
/// <see cref="TimeoutException" /> upon expiration of this deadline.<br/>
/// The dispatch of a one-way request cannot be canceled since the invocation typically completes before this dispatch
/// starts; as a result, for a one-way request, the deadline must be enforced by a <see cref="DeadlineMiddleware"/>.
/// <br/>
/// If the server installs a <see cref="DeadlineMiddleware"/>, this deadline middleware decodes the deadline and
/// enforces it. In the unlikely event the middleware detects the expiration of the deadline before this interceptor,
/// the invocation will fail with a <see cref="DispatchException"/> carrying status code
/// <see cref="StatusCode.DeadlineExpired"/>.<br/>
/// The deadline interceptor must be installed before any interceptor than can run multiple times per request. In
/// particular, it must be installed before the retry interceptor.</remarks>
public class DeadlineInterceptor : IInvoker
{
    private readonly bool _alwaysEnforceDeadline;
    private readonly IInvoker _next;
    private readonly TimeSpan _defaultTimeout;

    /// <summary>Constructs a Deadline interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="defaultTimeout">The default timeout. When not infinite, the interceptor adds a deadline to requests
    /// without a deadline.</param>
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
            throw new TimeoutException("The request deadline has expired.");
        }

        if (deadline != DateTime.MaxValue)
        {
            request.Fields = request.Fields.With(
                RequestFieldKey.Deadline,
                (ref SliceEncoder encoder) => encoder.EncodeTimeStamp(deadline));
        }

        return timeout is null ? _next.InvokeAsync(request, cancellationToken) : PerformInvokeAsync(timeout.Value);

        async Task<IncomingResponse> PerformInvokeAsync(TimeSpan timeout)
        {
            using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutTokenSource.CancelAfter(timeout);

            try
            {
                return await _next.InvokeAsync(request, timeoutTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == timeoutTokenSource.Token)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("The request deadline has expired.");
            }
        }
    }
}
