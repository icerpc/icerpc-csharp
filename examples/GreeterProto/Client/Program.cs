// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using IceRpc;
using System.Buffers;
using System.IO.Pipelines;
using VisitorCenter;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

{
    // Construct an outgoing request to the icerpc:/greeter service, and dispose this request before shutting down.
    using var request = new OutgoingRequest(new ServiceAddress(new Uri("icerpc:/greeter")))
    {
        Operation = "Greet", // the rpc name in the proto file

        // We create a PipeReader from the Protobuf message.
        Payload = PipeReader.Create(
            new ReadOnlySequence<byte>(new GreetRequest { Name = Environment.UserName }.ToByteArray()))
    };

    // Make the invocation: we send the request using the connection and then wait for the response.
    IncomingResponse response = await connection.InvokeAsync(request);

    if (response.StatusCode == StatusCode.Success)
    {
        // We decode the response payload. It's an async read but unfortunately Protobuf does'nt provide an async API.
        var greetResponse = new GreetResponse();

        // leaveOpen: true because MergeFrom doesn't dispose the stream.
        greetResponse.MergeFrom(response.Payload.AsStream(leaveOpen: true));
        response.Payload.Complete();
        Console.WriteLine(greetResponse.Greeting);
    }
    else
    {
        Console.WriteLine($"request failed: {response.StatusCode}");
    }
}

await connection.ShutdownAsync();
