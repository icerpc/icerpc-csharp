// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using IceRpc;
using System.Buffers;
using System.IO.Pipelines;
using VisitorCenter;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

string greeting = await GreetAsync(Environment.UserName);
Console.WriteLine(greeting);

await connection.ShutdownAsync();

// Create the request to the greeter and then await and decode the response.
async Task<string> GreetAsync(string name)
{
    // Construct an outgoing request to the icerpc:/greeter service.
    using var request = new OutgoingRequest(new ServiceAddress(new Uri("icerpc:/greeter")))
    {
        Operation = "Greet", // the rpc name in the proto file

        // Create a PipeReader from the Protobuf message.
        Payload = PipeReader.Create(
            new ReadOnlySequence<byte>(new GreetRequest { Name = name }.ToByteArray()))
    };

    // Make the invocation: we send the request using the connection and then wait for the response.
    IncomingResponse response = await connection.InvokeAsync(request);

    if (response.StatusCode == StatusCode.Success)
    {
        // Convert the response payload into a stream for decoding with Protobuf.
        using Stream payloadStream = response.Payload.AsStream();

        var greetResponse = new GreetResponse();
        greetResponse.MergeFrom(payloadStream);
        return greetResponse.Greeting;
    }
    else
    {
        // Convert the response into a dispatch exception.
        throw new DispatchException(response.StatusCode, response.ErrorMessage);
    }
}
