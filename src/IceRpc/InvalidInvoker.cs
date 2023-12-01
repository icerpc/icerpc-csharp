// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Implements the <see cref="IInvoker" /> interface by always throwing
/// <see cref="InvalidOperationException" />.</summary>
public class InvalidInvoker : IInvoker
{
    /// <summary>Gets the singleton instance of <see cref="InvalidInvoker" />.</summary>
    public static IInvoker Instance { get; } = new InvalidInvoker();

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Invalid invoker.");

    private InvalidInvoker()
    {
    }
}
