// Copyright (c) ZeroC, Inc.

using IceRpc;

namespace HelloCoreExample;

/// <summary>Implements a dispatcher for the sayHelloCore operation.</summary>
internal class Chatbot : IDispatcher
{
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Operation == "sayHelloCore")
        {
            string name = await StringCodec.DecodePayloadStringAsync(request.Payload);
            Console.WriteLine($"{name} says hello!");

            return new OutgoingResponse(request) { Payload = StringCodec.EncodeString($"Hello, {name}!") };
        }
        else
        {
            // We only implement sayHelloCore.
            throw new DispatchException(StatusCode.OperationNotFound);
        }
    }
}
