// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Adapts a function to the <see cref="IInvoker" /> interface.</summary>
public class InlineInvoker : IInvoker
{
    private readonly Func<OutgoingRequest, CancellationToken, Task<IncomingResponse>> _function;

    /// <summary>Constructs an InlineInvoker using a function.</summary>
    /// <param name="function">The function that implements the invoker's InvokerAsync method.</param>
    public InlineInvoker(Func<OutgoingRequest, CancellationToken, Task<IncomingResponse>> function) =>
        _function = function;

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default) =>
        _function(request, cancellationToken);
}
