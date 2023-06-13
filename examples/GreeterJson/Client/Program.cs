// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.Buffers;
using System.IO.Pipelines;
using System.Text.Json;
using VisitorCenter;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

string greeting = await GreetAsync(Environment.UserName);
Console.WriteLine(greeting);

await connection.ShutdownAsync();

// Create the request to the greeter and then await and decode the response.
async Task<string> GreetAsync(string name)
{
    var pipe = new Pipe();

    using var jsonWriter = new Utf8JsonWriter(pipe.Writer);
    JsonSerializer.Serialize(jsonWriter, new GreetRequest { Name = name });
    pipe.Writer.Complete();

    // Construct an outgoing request to the icerpc:/greeter service.
    using var request = new OutgoingRequest(new ServiceAddress(new Uri("icerpc:/greeter")))
    {
        Operation = "Greet",
        // The PipeReader to read the Json message.
        Payload = pipe.Reader
    };

    // Make the invocation: we send the request using the connection and then wait for the response.
    IncomingResponse response = await connection.InvokeAsync(request);

    if (response.StatusCode == StatusCode.Success)
    {
        ReadResult readResult;
        while (true)
        {
            readResult = await response.Payload.ReadAsync();
            if (readResult.IsCompleted)
            {
                GreetResponse greeterResponse = DecodeResponse(readResult.Buffer);
                await response.Payload.CompleteAsync();
                return greeterResponse.Greeting;
            }
            else
            {
                response.Payload.AdvanceTo(readResult.Buffer.Start);
            }
        }
    }
    else
    {
        // Convert the response into a dispatch exception.
        throw new DispatchException(response.StatusCode, response.ErrorMessage);
    }

    static GreetResponse DecodeResponse(ReadOnlySequence<byte> buffer)
    {
        var jsonReader = new Utf8JsonReader(buffer);
        if (JsonSerializer.Deserialize(ref jsonReader, typeof(GreetResponse)) is not GreetResponse greetResponse)
        {
            throw new InvalidDataException("unable to decode GreetResponse");
        }
        return greetResponse;
    }
}
