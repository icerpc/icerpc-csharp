// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc.Tests.Common;

/// <summary>An invoker that transforms an outgoing request into an incoming request, dispatches it to the dispatcher
/// configured with the invoker and finally transforms the outgoing response to an incoming response that is returned to
/// the caller.</summary>
public sealed class ColocInvoker : IInvoker
{
    private readonly IDispatcher _dispatcher;

    /// <summary>Constructs a colocated invoker.</summary>
    /// <param name="dispatcher">The dispatcher to forward requests to.</param>
    public ColocInvoker(IDispatcher dispatcher) => _dispatcher = dispatcher;

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        // Create the incoming request from the outgoing request to forward to the dispatcher.
        using var incomingRequest = new IncomingRequest(request.Protocol, FakeConnectionContext.Instance)
        {
            Payload = await GetPayloadFromOutgoingFrameAsync(request, cancellationToken),
            Path = request.ServiceAddress.Path,
            Operation = request.Operation
        };

        // Dispatch the request.
        OutgoingResponse outgoingResponse;
        try
        {
            outgoingResponse = await _dispatcher.DispatchAsync(incomingRequest, cancellationToken);
        }
        catch (Exception exception)
        {
            outgoingResponse = new OutgoingResponse(
                incomingRequest,
                StatusCode.UnhandledException,
                message: null,
                exception);
        }

        // Create the incoming response from the outgoing response returned by the dispatcher.
        PipeReader payload = await GetPayloadFromOutgoingFrameAsync(outgoingResponse, cancellationToken);
        outgoingResponse.Payload = InvalidPipeReader.Instance;
        outgoingResponse.PayloadContinuation = null;
        return new IncomingResponse(
            request,
            FakeConnectionContext.Instance,
            outgoingResponse.StatusCode,
            outgoingResponse.ErrorMessage)
        {
            Payload = payload
        };

        static async Task<PipeReader> GetPayloadFromOutgoingFrameAsync(
            OutgoingFrame outgoingFrame,
            CancellationToken cancellationToken)
        {
            PipeReader payload;
            if (outgoingFrame.PayloadContinuation is null)
            {
                payload = outgoingFrame.Payload;
            }
            else
            {
                var pipe = new Pipe();
                await outgoingFrame.Payload.CopyToAsync(pipe.Writer, cancellationToken);
                await outgoingFrame.PayloadContinuation.CopyToAsync(pipe.Writer, cancellationToken);
                pipe.Writer.Complete();
                payload = pipe.Reader;
            }
            return payload;
        }
    }
}
