// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>An invoker sends outgoing requests and returns incoming responses.</summary>
public interface IInvoker
{
    /// <summary>Sends an outgoing request and returns the corresponding incoming response.</summary>
    /// <param name="request">The outgoing request being sent.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The corresponding <see cref="IncomingResponse"/>.</returns>
    Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default);
}

/// <summary>Adapts an invoker delegate to the <see cref="IInvoker"/> interface.</summary>
public class InlineInvoker : IInvoker
{
    private readonly Func<OutgoingRequest, CancellationToken, Task<IncomingResponse>> _function;

    /// <summary>Constructs an InlineInvoker using a delegate.</summary>
    /// <param name="function">The function that implements the invoker's InvokerAsync method.</param>
    public InlineInvoker(Func<OutgoingRequest, CancellationToken, Task<IncomingResponse>> function) =>
        _function = function;

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        _function(request, cancel);
}

/// <summary>A trivial invoker that always throws <see cref="InvalidOperationException"/>.</summary>
public class NullInvoker : IInvoker
{
    /// <summary>Gets the unique instance of this class.</summary>
    public static NullInvoker Instance { get; } = new();

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        throw new InvalidOperationException("invoked null invoker");

    private NullInvoker()
    {
        // Ensures it's a singleton.
    }
}
