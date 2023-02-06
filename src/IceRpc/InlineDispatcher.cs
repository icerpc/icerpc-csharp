// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Adapts a dispatcher delegate to the <see cref="IDispatcher" /> interface.</summary>
public class InlineDispatcher : IDispatcher
{
    private readonly Func<IncomingRequest, CancellationToken, ValueTask<OutgoingResponse>> _function;

    /// <summary>Constructs an InlineDispatcher using a delegate.</summary>
    /// <param name="function">The function that implements the dispatcher's DispatchAsync method.</param>
    public InlineDispatcher(Func<IncomingRequest, CancellationToken, ValueTask<OutgoingResponse>> function) =>
        _function = function;

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken = default) =>
        _function(request, cancellationToken);
}
