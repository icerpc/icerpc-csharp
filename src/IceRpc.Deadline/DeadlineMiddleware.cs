// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace IceRpc.Deadline;

/// <summary>The deadline middleware decodes the deadline field into the deadline feature.</summary>
public class DeadlineMiddleware : IDispatcher
{
    private readonly IDispatcher _next;

    /// <summary>Constructs a deadline middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public DeadlineMiddleware(IDispatcher next) => _next = next;

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel = default)
    {
        // not found returns 0
        long value = request.Fields.DecodeValue(
            RequestFieldKey.Deadline,
            (ref SliceDecoder decoder) => decoder.DecodeVarInt62());

        if (value > 0)
        {
            request.Features = request.Features.With<IDeadlineFeature>(
                new DeadlineFeature
                {
                    Value = DateTime.UnixEpoch + TimeSpan.FromMilliseconds(value)
                });
        }

        return _next.DispatchAsync(request, cancel);
    }
}
