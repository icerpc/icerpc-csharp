// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Buffers;
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
        if (request.Operation == "Greet")
        {
            ReadResult readResult;
            while (true)
            {
                readResult = await request.Payload.ReadAsync(cancellationToken);
                if (readResult.IsCompleted)
                {
                    GreetRequest greetRequest = DecodeGreetRequest(readResult.Buffer);
                    await request.Payload.CompleteAsync();
                    Console.WriteLine($"Dispatching Greet request {{ name = '{greetRequest.Name}' }}");

                    var pipe = new Pipe();
                    using var jsonWriter = new Utf8JsonWriter(pipe.Writer);
                    JsonSerializer.Serialize(
                        jsonWriter,
                        new GreetResponse { Greeting = $"Hello, {greetRequest.Name}!" });
                    pipe.Writer.Complete();

                    return new OutgoingResponse(request)
                    {
                        // Create a PipeReader from the Json message.
                        Payload = pipe.Reader
                    };
                }
                else
                {
                    request.Payload.AdvanceTo(readResult.Buffer.Start);
                }
            }
        }
        else
        {
            // We only implement Greet.
            throw new DispatchException(StatusCode.OperationNotFound);
        }

        static GreetRequest DecodeGreetRequest(ReadOnlySequence<byte> buffer)
        {
            var jsonReader = new Utf8JsonReader(buffer);
            if (JsonSerializer.Deserialize(ref jsonReader, typeof(GreetRequest)) is not GreetRequest greetRequest)
            {
                throw new InvalidDataException("unable to decode GreetRequest");
            }
            return greetRequest;
        }
    }
}
