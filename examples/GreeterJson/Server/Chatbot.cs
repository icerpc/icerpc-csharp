// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text.Json;
using VisitorCenter;

namespace GreeterJsonServer;

/// <summary>Implements a dispatcher for the Greet operation.</summary>
internal class Chatbot : IDispatcher
{
    public async ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Operation == "greet")
        {
            ReadResult readResult = await request.Payload.ReadAtLeastAsync(int.MaxValue, cancellationToken);
            Debug.Assert(readResult.IsCompleted);

            GreetRequest greetRequest = DecodeGreetRequest(readResult.Buffer);
            request.Payload.Complete();
            Console.WriteLine($"Dispatching Greet request {{ name = '{greetRequest.Name}' }}");

            var pipe = new Pipe();
            using var jsonWriter = new Utf8JsonWriter(pipe.Writer);
            JsonSerializer.Serialize(
                jsonWriter,
                new GreetResponse { Greeting = $"Hello, {greetRequest.Name}!" });
            pipe.Writer.Complete();

            return new OutgoingResponse(request)
            {
                // Use the PipeReader holding the JSON message as the response payload.
                Payload = pipe.Reader
            };
        }
        else
        {
            // We only implement Greet.
            throw new DispatchException(StatusCode.OperationNotFound);
        }

        static GreetRequest DecodeGreetRequest(ReadOnlySequence<byte> buffer)
        {
            var jsonReader = new Utf8JsonReader(buffer);
            return JsonSerializer.Deserialize(ref jsonReader, typeof(GreetRequest)) is GreetRequest greetRequest ?
                greetRequest : throw new InvalidDataException($"Unable to decode {nameof(GreetRequest)}.");
        }
    }
}
