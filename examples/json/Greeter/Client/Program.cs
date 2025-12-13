// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text.Json;
using VisitorCenter; // for GreetRequest and GreetResponse

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

string greeting = await GreetAsync(Environment.UserName);
Console.WriteLine(greeting);

await connection.ShutdownAsync();

// Create the request to the greeter and then await and decode the response.
async Task<string> GreetAsync(string name)
{
    // Create a PipeReader holding the JSON request message.
    var pipe = new Pipe();
    var greetRequest = new GreetRequest { Name = name };
    await JsonSerializer.SerializeAsync(pipe.Writer, greetRequest);
    pipe.Writer.Complete();

    // Construct an outgoing request to the icerpc:/greeter service.
    // The payload is the PipeReader of our pipe.
    using var request = new OutgoingRequest(new ServiceAddress(new Uri("icerpc:/greeter")))
    {
        Operation = "greet",
        Payload = pipe.Reader // request takes ownership of the PipeReader
    };

    // Make the invocation: we send the request using the connection and then wait for the response.
    IncomingResponse response = await connection.InvokeAsync(request);

    if (response.StatusCode == StatusCode.Ok)
    {
        // Deserialize the response payload.
        GreetResponse greeterResponse = await JsonSerializer.DeserializeAsync<GreetResponse>(response.Payload);
        response.Payload.Complete(); // DeserializeAsync reads to completion but does not complete the PipeReader.

        return greeterResponse.Greeting;
    }
    else
    {
        // IceRPC guarantees the error message is non-null when StatusCode > Ok.
        Debug.Assert(response.ErrorMessage is not null);
        throw new DispatchException(response.StatusCode, response.ErrorMessage);
    }
}
