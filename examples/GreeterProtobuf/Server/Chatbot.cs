// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using IceRpc;
using System.Buffers;
using System.IO.Pipelines;
using VisitorCenter;

namespace GreeterProtobufServer;

/// <summary>Implements a dispatcher for the Greet operation.</summary>
internal class Chatbot : IDispatcher
{
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        // The rpc name in the proto file.
        if (request.Operation == "Greet")
        {
            // Convert the request payload into a stream for decoding with Protobuf.
            using Stream payloadStream = request.Payload.AsStream();

            var greetRequest = new GreetRequest();
            greetRequest.MergeFrom(payloadStream);
            Console.WriteLine($"Dispatching Greet request {{ name = '{greetRequest.Name}' }}");

            return new(
                new OutgoingResponse(request)
                {
                    // Create a PipeReader from the Protobuf message.
                    Payload = PipeReader.Create(
                            new ReadOnlySequence<byte>(
                                new GreetResponse { Greeting = $"Hello, {greetRequest.Name}!" }.ToByteArray()))
                });
        }
        else
        {
            // We only implement Greet.
            throw new DispatchException(StatusCode.OperationNotFound);
        }
    }
}
