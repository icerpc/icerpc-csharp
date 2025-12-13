// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.IO.Pipelines;
using System.Text.Json;
using VisitorCenter; // for GreetRequest and GreetResponse

namespace GreeterServer;

/// <summary>Implements a dispatcher for the Greet operation.</summary>
internal class Chatbot : IDispatcher
{
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Operation == "greet")
        {
            // Deserialize the request payload.
            GreetRequest greetRequest = await JsonSerializer.DeserializeAsync<GreetRequest>(
                request.Payload,
                cancellationToken: cancellationToken);
            request.Payload.Complete(); // DeserializeAsync reads to completion but does not complete the PipeReader.

            Console.WriteLine($"Dispatching Greet request {{ name = '{greetRequest.Name}' }}");

            // Create the greet response.
            var greetResponse = new GreetResponse
            {
                Greeting = $"Hello, {greetRequest.Name}!"
            };

            // Create a PipeReader holding the JSON response message.
            var pipe = new Pipe();
            await JsonSerializer.SerializeAsync(pipe.Writer, greetResponse, cancellationToken: cancellationToken);
            pipe.Writer.Complete();

            // Return the response.
            return new OutgoingResponse(request)
            {
                Payload = pipe.Reader // the OutgoingResponse takes ownership of the PipeReader
            };
        }
        else
        {
            // We only implement Greet.
            return new OutgoingResponse(request, StatusCode.NotImplemented);
        }
    }
}
