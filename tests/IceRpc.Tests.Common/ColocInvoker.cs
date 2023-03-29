// Copyright (c) ZeroC, Inc.

// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc.Tests.Common;

/// <summary>An invoker that transforms an outgoing request into an incoming request, dispatches it to the
/// dispatcher configured with the invoker and finally transforms the outgoing response to an incoming response that
/// is returned to the caller.</summary>
public sealed class ColocInvoker : IInvoker
{
    private readonly IDispatcher _dispatcher;

    /// <summary>Constructs a collocated invoker.</summary>
    /// <param name="dispatcher">The dispatcher to forward requests to.</param>
    public ColocInvoker(IDispatcher dispatcher) => _dispatcher = dispatcher;

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        // Payload continuation are not supported for now.
        using var incomingRequest = new IncomingRequest(Protocol.IceRpc, FakeConnectionContext.Instance)
        {
            Payload = request.Payload,
            Path = request.ServiceAddress.Path,
            Operation = request.Operation
        };

        // Dispatch the request.
        OutgoingResponse outgoingResponse = await _dispatcher.DispatchAsync(incomingRequest, cancellationToken);

        // Payload continuation are not supported for now.
        PipeReader payload = outgoingResponse.Payload;
        outgoingResponse.Payload = InvalidPipeReader.Instance;
        return new IncomingResponse(request, FakeConnectionContext.Instance)
        {
            Payload = payload
        };
    }
}
