// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Features;
using System.Buffers;
using ZeroC.Slice;

namespace IceRpc.Deadline;

/// <summary>Represents a middleware that decodes deadline fields into deadline features. When the decoded deadline
/// expires, this middleware cancels the dispatch and returns an <see cref="OutgoingResponse" /> with status code
/// <see cref="StatusCode.DeadlineExceeded" />.</summary>
/// <remarks>If the peer-encoded deadline is too far in the future for this middleware to enforce (more than
/// ~24.8 days from now), the request is rejected with <see cref="StatusCode.NotSupported" /> instead.</remarks>
/// <seealso cref="DeadlineRouterExtensions"/>
/// <seealso cref="DeadlineDispatcherBuilderExtensions"/>
public class DeadlineMiddleware : IDispatcher
{
    // The maximum supported timeout (int.MaxValue ms, ~24.8 days). This is the maximum delay
    // CancellationTokenSource.CancelAfter accepts.
    private static readonly TimeSpan _maxSupportedTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

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
        // Check explicit field presence rather than relying on a decoded-value sentinel.
        if (request.Fields.TryGetValue(RequestFieldKey.Deadline, out ReadOnlySequence<byte> value))
        {
            var decoder = new SliceDecoder(value, SliceEncoding.Slice2);
            DateTime deadline = decoder.DecodeTimeStamp();
            TimeSpan timeout = deadline - _timeProvider.GetUtcNow().UtcDateTime;

            if (timeout <= TimeSpan.Zero)
            {
                return new(new OutgoingResponse(
                    request,
                    StatusCode.DeadlineExceeded,
                    "The request deadline has expired."));
            }

            // Reject a peer-encoded deadline that exceeds what this middleware can enforce. Silently clamping
            // a deadline the client asked for to a smaller value the implementation supports is worse than
            // failing cleanly.
            if (timeout > _maxSupportedTimeout)
            {
                return new(new OutgoingResponse(
                    request,
                    StatusCode.NotSupported,
                    $"The request deadline exceeds the maximum timeout supported by this server: {_maxSupportedTimeout}."));
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
