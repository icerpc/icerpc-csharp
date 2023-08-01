// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Json;
using System.Diagnostics;
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
        Operation = "greet",
        // Use a PipeReader holding the JSON message as the request payload.
        Payload = PipeReaderJsonSerializer.SerializeToPipeReader(new GreetRequest { Name = name })
    };

    // Make the invocation: we send the request using the connection and then wait for the response.
    IncomingResponse response = await connection.InvokeAsync(request);

    if (response.StatusCode == StatusCode.Success)
    {
        GreetResponse greeterResponse = await PipeReaderJsonSerializer.DeserializeAsync<GreetResponse>(
            response.Payload);
        return greeterResponse.Greeting;
    }
    else
    {
        // IceRPC guarantees the error message is non-null when StatusCode > Success.
        Debug.Assert(response.ErrorMessage is not null);
        throw new DispatchException(response.StatusCode, response.ErrorMessage);
    }
}
