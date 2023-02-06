// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace IceRpc.Deadline;

/// <summary>The deadline middleware decodes the deadline field into the deadline feature. When the deadline expires,
/// the dispatch is canceled and the middleware throws <see cref="DispatchException" /> with status code
/// <see cref="StatusCode.DeadlineExpired" />.</summary>
public class DeadlineMiddleware : IDispatcher
{
    private readonly IDispatcher _next;

    /// <summary>Constructs a deadline middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public DeadlineMiddleware(IDispatcher next) => _next = next;

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken = default)
    {
        TimeSpan? timeout = null;

        // not found returns 0
        long value = request.Fields.DecodeValue(
            RequestFieldKey.Deadline,
            (ref SliceDecoder decoder) => decoder.DecodeVarInt62());

        if (value > 0)
        {
            DateTime deadline = DateTime.UnixEpoch + TimeSpan.FromMilliseconds(value);
            timeout = deadline - DateTime.UtcNow;

            if (timeout <= TimeSpan.Zero)
            {
                throw new DispatchException(StatusCode.DeadlineExpired, "The request deadline has expired.");
            }

            request.Features = request.Features.With<IDeadlineFeature>(new DeadlineFeature(deadline));
        }

        return timeout is null ? _next.DispatchAsync(request, cancellationToken) : PerformDispatchAsync(timeout.Value);

        async ValueTask<OutgoingResponse> PerformDispatchAsync(TimeSpan timeout)
        {
            using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutTokenSource.CancelAfter(timeout);

            try
            {
                return await _next.DispatchAsync(request, timeoutTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == timeoutTokenSource.Token)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new DispatchException(StatusCode.DeadlineExpired, "The request deadline has expired.");
            }
        }
    }
}
