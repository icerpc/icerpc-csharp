// Copyright (c) ZeroC, Inc.

using IceRpc;

namespace GreeterCoreExample;

/// <summary>Implements a dispatcher for the greetCore operation.</summary>
internal class Chatbot : IDispatcher
{
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Operation == "greetCore")
        {
            string name = await StringCodec.DecodePayloadStringAsync(request.Payload);
            Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");

            return new OutgoingResponse(request) { Payload = StringCodec.EncodeString($"Greeter, {name}!") };
        }
        else
        {
            // We only implement greetCore.
            throw new DispatchException(StatusCode.OperationNotFound);
        }
    }
}
