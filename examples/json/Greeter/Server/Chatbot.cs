// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Json;
using VisitorCenter;

namespace GreeterServer;

/// <summary>Implements a dispatcher for the Greet operation.</summary>
internal class Chatbot : IDispatcher
{
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Operation == "greet")
        {
            GreetRequest greetRequest = await PipeReaderJsonSerializer.DeserializeAsync<GreetRequest>(
                request.Payload,
                cancellationToken: cancellationToken);
            Console.WriteLine($"Dispatching Greet request {{ name = '{greetRequest.Name}' }}");
            return new OutgoingResponse(request)
            {
                // Use a PipeReader holding the JSON message as the response payload.
                Payload = PipeReaderJsonSerializer.SerializeToPipeReader(
                    new GreetResponse { Greeting = $"Hello, {greetRequest.Name}!" })
            };
        }
        else
        {
            // We only implement Greet.
            return new OutgoingResponse(request, StatusCode.NotImplemented);
        }
    }
}
