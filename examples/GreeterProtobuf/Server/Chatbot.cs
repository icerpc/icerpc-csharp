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
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        // The rpc name in the proto file.
        if (request.Operation == "Greet")
        {
            var greetRequest = new GreetRequest();
            await greetRequest.MergeFromAsync(request.Payload);
            Console.WriteLine($"Dispatching Greet request {{ name = '{greetRequest.Name}' }}");

            return new OutgoingResponse(request)
                {
                    // Create a PipeReader from the Protobuf message.
                    Payload = new GreetResponse { Greeting = $"Hello, {greetRequest.Name}!" }.ToPipeReader()
                };
        }
        else
        {
            // We only implement Greet.
            throw new DispatchException(StatusCode.OperationNotFound);
        }
    }
}
