// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Features;
using ZeroC.Slice;

namespace IceRpc.Deadline;

/// <summary>Represents a middleware that decodes deadline fields into deadline features. When the decoded deadline
/// expires, this middleware cancels the dispatch and returns an <see cref="OutgoingResponse" /> with status code
/// <see cref="StatusCode.DeadlineExceeded" />.</summary>
/// <seealso cref="DeadlineRouterExtensions"/>
/// <seealso cref="DeadlineDispatcherBuilderExtensions"/>
public class DeadlineMiddleware : IDispatcher
{
    private readonly IDispatcher _next;

    /// <summary>Constructs a deadline middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public DeadlineMiddleware(IDispatcher next) => _next = next;

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        CancellationToken cancellationToken = default)
    {
        TimeSpan? timeout = null;

        // not found returns default == DateTime.MinValue.
        DateTime deadline = request.Fields.DecodeValue(
            RequestFieldKey.Deadline,
            (ref SliceDecoder decoder) => decoder.DecodeTimeStamp());

        if (deadline != DateTime.MinValue)
        {
            timeout = deadline - DateTime.UtcNow;

            if (timeout <= TimeSpan.Zero)
            {
                return new(new OutgoingResponse(
                    request,
                    StatusCode.DeadlineExceeded,
                    "The request deadline has expired."));
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
                return new OutgoingResponse(request, StatusCode.DeadlineExceeded, "The request deadline has expired.");
            }
        }
    }
}
