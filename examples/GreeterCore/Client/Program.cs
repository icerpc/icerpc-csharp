// Copyright (c) ZeroC, Inc.

using GreeterCore;
using IceRpc;
using System.Diagnostics;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

string greeting = await GreetAsync(Environment.UserName);
Console.WriteLine(greeting);

await connection.ShutdownAsync();

// Create the request to the greeter and then await and decode the response.
async Task<string> GreetAsync(string name)
{
    // Construct an outgoing request for the icerpc protocol.
    using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
    {
        Operation = "greet",
        Payload = StringCodec.EncodeString(name)
    };

    // Make the invocation: we send the request using the client connection and then wait for the response. Since the
    // client connection is not connected yet, this call also connects it.
    IncomingResponse response = await connection.InvokeAsync(request);

    // When the response's status code is Ok, we decode its payload.
    if (response.StatusCode == StatusCode.Ok)
    {
        return await StringCodec.DecodePayloadStringAsync(response.Payload);
    }
    else
    {
        // IceRPC guarantees the error message is non-null when StatusCode > Ok.
        Debug.Assert(response.ErrorMessage is not null);
        throw new DispatchException(response.StatusCode, response.ErrorMessage);
    }
}
