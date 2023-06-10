// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using IceRpc;
using System.Buffers;
using System.IO.Pipelines;
using VisitorCenter;

namespace GreeterProtoServer;

/// <summary>Implements a dispatcher for the Greet operation.</summary>
internal class Chatbot : IDispatcher
{
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        // The rpc name in the proto file.
        if (request.Operation == "Greet")
        {
            // We decode the request payload. It's an async read but unfortunately Protobuf doesn't provide an async
            // API.
            var greetRequest = new GreetRequest();

            // leaveOpen: true because MergeFrom doesn't dispose the stream.
            greetRequest.MergeFrom(request.Payload.AsStream(leaveOpen: true));
            await request.Payload.CompleteAsync();
            Console.WriteLine($"Dispatching Greet request {{ name = '{greetRequest.Name}' }}");

            return new OutgoingResponse(request)
            {
                // We create a PipeReader from the Protobuf message.
                Payload = PipeReader.Create(
                        new ReadOnlySequence<byte>(
                            new GreetResponse { Greeting = $"Hello, {greetRequest.Name}!" }.ToByteArray()))
            };
        }
        else
        {
            // We only implement Greet.
            throw new DispatchException(StatusCode.OperationNotFound);
        }
    }
}
