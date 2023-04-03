// Copyright (c) ZeroC, Inc.

using GreeterCoreExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Construct an outgoing request for the icerpc protocol.
using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
{
    Operation = "greetCore",
    Payload = StringCodec.EncodeString(Environment.UserName)
};

// Make the invocation: we send the request using the client connection and then wait for the response. Since the client
// connection is not connected yet, this call also connects the connection.
IncomingResponse response = await connection.InvokeAsync(request);

// When the response's status code is Success, we decode its payload.
if (response.StatusCode == StatusCode.Success)
{
    string greeting = await StringCodec.DecodePayloadStringAsync(response.Payload);

    Console.WriteLine(greeting);
}
else
{
    Console.WriteLine($"request failed: {response.StatusCode}");
}

await connection.ShutdownAsync();
