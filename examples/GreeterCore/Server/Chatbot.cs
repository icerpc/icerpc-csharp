// Copyright (c) ZeroC, Inc.

using IceRpc;
using GreeterCore;

namespace GreeterCoreServer;

/// <summary>Implements a dispatcher for the greet operation.</summary>
internal class Chatbot : IDispatcher
{
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Operation == "greet")
        {
            string name = await StringCodec.DecodePayloadStringAsync(request.Payload);
            Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");

            return new OutgoingResponse(request) { Payload = StringCodec.EncodeString($"Hello, {name}!") };
        }
        else
        {
            // We only implement greet.
            throw new DispatchException(StatusCode.OperationNotFound);
        }
    }
}
