// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace IceRpc.Deadline;

/// <summary>The deadline middleware decodes the deadline field into the deadline feature. When the deadline expires,
/// the dispatch is canceled and the middleware throws <see cref="DispatchException"/> with the
/// <see cref="DispatchErrorCode.DeadlineExpired"/> error code.</summary>
public class DeadlineMiddleware : IDispatcher
{
    private readonly IDispatcher _next;

    /// <summary>Constructs a deadline middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public DeadlineMiddleware(IDispatcher next) => _next = next;

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel = default)
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
                throw new DispatchException("the request deadline has expired", DispatchErrorCode.DeadlineExpired);
            }

            request.Features = request.Features.With<IDeadlineFeature>(
                new DeadlineFeature(DateTime.UnixEpoch + TimeSpan.FromMilliseconds(value)));
        }

        return timeout is null ? _next.DispatchAsync(request, cancel) : PerformDispatchAsync(timeout.Value);

        async ValueTask<OutgoingResponse> PerformDispatchAsync(TimeSpan timeout)
        {
            using var tokenSource = new CancellationTokenSource(timeout);
            using CancellationTokenRegistration _ = cancel.Register(tokenSource.Cancel);

            try
            {
                return await _next.DispatchAsync(request, tokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == tokenSource.Token)
            {
                cancel.ThrowIfCancellationRequested();
                throw new DispatchException("the request deadline has expired", DispatchErrorCode.DeadlineExpired);
            }
        }
    }
}
