// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc.Deadline;

/// <summary>The deadline interceptor makes sure the cancellation token is cancelable, and encodes the deadline feature
/// into the deadline field.</summary>
public class DeadlineInterceptor : IInvoker
{
    private readonly IInvoker _next;

    /// <summary>Constructs a deadline interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    public DeadlineInterceptor(IInvoker next) => _next = next;

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default)
    {
        if (request.Features.Get<IDeadlineFeature>() is IDeadlineFeature deadlineFeature &&
            deadlineFeature.Value != DateTime.MaxValue)
        {
            if (!cancel.CanBeCanceled)
            {
                throw new InvalidOperationException(
                    "the request's cancellation token must be cancelable when a deadline is set");
            }

            long deadline = (long)(deadlineFeature.Value - DateTime.UnixEpoch).TotalMilliseconds;
            request.Fields = request.Fields.With(
                RequestFieldKey.Deadline,
                (ref SliceEncoder encoder) => encoder.EncodeVarInt62(deadline));
        }

        return _next.InvokeAsync(request, cancel);
    }
}
