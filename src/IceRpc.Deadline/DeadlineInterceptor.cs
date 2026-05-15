// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Features;
using ZeroC.Slice;

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
/// the invocation will return an <see cref="OutgoingResponse"/> carrying status code
/// <see cref="StatusCode.DeadlineExceeded"/>.<br/>
/// The deadline interceptor must be installed before any interceptor than can run multiple times per request. In
/// particular, it must be installed before the retry interceptor.</remarks>
/// <seealso cref="DeadlinePipelineExtensions"/>
/// <seealso cref="DeadlineInvokerBuilderExtensions"/>
public class DeadlineInterceptor : IInvoker
{
    // The maximum supported timeout (int.MaxValue ms, ~24.8 days). This is the maximum delay
    // CancellationTokenSource.CancelAfter accepts.
    private static readonly TimeSpan _maxSupportedTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

    private readonly bool _alwaysEnforceDeadline;
    private readonly IInvoker _next;
    private readonly TimeSpan _defaultTimeout;
    private readonly TimeProvider _timeProvider;

    /// <summary>Constructs a Deadline interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="defaultTimeout">The default timeout applied to requests without a deadline. Must be a positive
    /// value not exceeding ~24.8 days, or <see cref="Timeout.InfiniteTimeSpan" /> to disable the default timeout
    /// entirely.</param>
    /// <param name="timeProvider">The optional time provider used to obtain the current time. If
    /// <see langword="null"/>, it uses <see cref="TimeProvider.System"/>.</param>
    /// <param name="alwaysEnforceDeadline">When <see langword="true" /> and the request carries a deadline, the
    /// interceptor always creates a cancellation token source to enforce this deadline. When <see langword="false" />
    /// and the request carries a deadline, the interceptor creates a cancellation token source to enforce this deadline
    /// only when the invocation's cancellation token cannot be canceled. The default value is <see langword="false" />.
    /// </param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="defaultTimeout" /> is neither
    /// <see cref="Timeout.InfiniteTimeSpan" /> nor a positive value within the supported range.</exception>
    public DeadlineInterceptor(IInvoker next, TimeSpan defaultTimeout, bool alwaysEnforceDeadline, TimeProvider? timeProvider = null)
    {
        if (defaultTimeout != Timeout.InfiniteTimeSpan &&
            (defaultTimeout <= TimeSpan.Zero || defaultTimeout > _maxSupportedTimeout))
        {
            throw new ArgumentException(
                $"The {nameof(defaultTimeout)} value must be Timeout.InfiniteTimeSpan or a positive value not exceeding {_maxSupportedTimeout}.",
                nameof(defaultTimeout));
        }

        _next = next;
        _alwaysEnforceDeadline = alwaysEnforceDeadline;
        _defaultTimeout = defaultTimeout;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default)
    {
        TimeSpan? timeout = null;
        DateTime deadline = DateTime.MaxValue;

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        if (request.Features.Get<IDeadlineFeature>() is IDeadlineFeature deadlineFeature)
        {
            // Normalize to UTC.
            deadline = deadlineFeature.Value == DateTime.MaxValue ?
                DateTime.MaxValue : deadlineFeature.Value.ToUniversalTime();
            if (deadline != DateTime.MaxValue && (_alwaysEnforceDeadline || !cancellationToken.CanBeCanceled))
            {
                timeout = deadline - now;
            }
        }
        else if (_defaultTimeout != Timeout.InfiniteTimeSpan)
        {
            timeout = _defaultTimeout;
            deadline = now + timeout.Value;
        }

        if (timeout is not null && timeout.Value <= TimeSpan.Zero)
        {
            throw new TimeoutException("The request deadline has expired.");
        }

        if (deadline != DateTime.MaxValue)
        {
            request.Fields = request.Fields.With(
                RequestFieldKey.Deadline,
                deadline,
                (ref SliceEncoder encoder, DateTime deadline) => encoder.EncodeTimeStamp(deadline));
        }

        return timeout is null ? _next.InvokeAsync(request, cancellationToken) : PerformInvokeAsync(timeout.Value);

        async Task<IncomingResponse> PerformInvokeAsync(TimeSpan timeout)
        {
            // Reject a caller-provided IDeadlineFeature whose remaining timeout exceeds what this interceptor
            // can enforce. Such deadlines are nonsensical in practice (~24.8 days), and silently clamping the
            // value the caller asked for is worse than failing cleanly.
            if (timeout > _maxSupportedTimeout)
            {
                throw new NotSupportedException(
                    $"The request deadline exceeds the maximum timeout supported by this interceptor: {_maxSupportedTimeout}.");
            }

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
