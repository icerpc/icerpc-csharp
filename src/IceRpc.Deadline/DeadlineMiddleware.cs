// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Features;
using System.Buffers;
using ZeroC.Slice.Codec;

namespace IceRpc.Deadline;

/// <summary>Represents a middleware that decodes deadline fields into deadline features. When the decoded deadline
/// expires, this middleware cancels the dispatch and returns an <see cref="OutgoingResponse" /> with status code
/// <see cref="StatusCode.DeadlineExceeded" />.</summary>
/// <remarks>A peer-encoded deadline whose computed remaining timeout exceeds the
/// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)" /> maximum (~24.8 days) is silently clamped to that
/// maximum. At this bound the deadline is effectively infinite for RPC purposes.</remarks>
/// <seealso cref="DeadlineRouterExtensions"/>
/// <seealso cref="DeadlineDispatcherBuilderExtensions"/>
public class DeadlineMiddleware : IDispatcher
{
    // The maximum delay CancellationTokenSource.CancelAfter(TimeSpan) accepts.
    private static readonly TimeSpan MaxCancelAfterDelay = TimeSpan.FromMilliseconds(int.MaxValue);

    private readonly IDispatcher _next;
    private readonly TimeProvider _timeProvider;

    /// <summary>Constructs a deadline middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    /// <param name="timeProvider">The optional time provider used to obtain the current time. If <see langword="null"/>, it uses
    /// <see cref="TimeProvider.System"/>.</param>
    public DeadlineMiddleware(IDispatcher next, TimeProvider? timeProvider = null)
    {
        _next = next;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        CancellationToken cancellationToken = default)
    {
        // Check explicit field presence rather than relying on a decoded-value sentinel: a peer encoding ticks=0
        // decodes to DateTime.MinValue, which would otherwise be indistinguishable from an absent field.
        if (request.Fields.TryGetValue(RequestFieldKey.Deadline, out ReadOnlySequence<byte> value))
        {
            DateTime deadline = value.DecodeSliceBuffer(
                (ref SliceDecoder decoder) => decoder.DecodeTimeStamp());
            TimeSpan timeout = deadline - _timeProvider.GetUtcNow().UtcDateTime;

            if (timeout <= TimeSpan.Zero)
            {
                return new(new OutgoingResponse(
                    request,
                    StatusCode.DeadlineExceeded,
                    "The request deadline has expired."));
            }

            // Clamp to CancelAfter's supported maximum. A peer-encoded deadline thousands of years in the future
            // would otherwise cause CancelAfter to throw ArgumentOutOfRangeException, surfacing as a generic
            // InternalError response. At this bound the deadline is effectively infinite for RPC purposes.
            if (timeout > MaxCancelAfterDelay)
            {
                timeout = MaxCancelAfterDelay;
            }

            request.Features = request.Features.With<IDeadlineFeature>(new DeadlineFeature(deadline));
            return PerformDispatchAsync(timeout);
        }

        return _next.DispatchAsync(request, cancellationToken);

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
                return new OutgoingResponse(request, StatusCode.DeadlineExceeded, "The request deadline has expired.");
            }
        }
    }
}
