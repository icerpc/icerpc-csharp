# IceRPC

IceRPC is a modular RPC framework that helps you build networked applications with minimal effort. The IceRpc assembly
and package represent the base assembly and package for the [C# implementation of IceRPC][icerpc-csharp].

[Package][package] | [Source code][source] | [Getting started][getting-started] | [Examples][examples] | [Documentation][docs] | [API reference][api]

## Sample Code

```csharp
// Client application

using GreeterCore; // for the StringCodec helper class
using IceRpc;

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
        // Convert the response into a dispatch exception.
        throw new DispatchException(response.StatusCode, response.ErrorMessage);
    }
}
```

```csharp
// Server application

using GreeterCore; // for the StringCodec helper class
using IceRpc;

// Create a server that will dispatch all requests to the same dispatcher, an instance of Chatbot.
await using var server = new Server(new Chatbot());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();

internal class Chatbot : IDispatcher
{
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Operation == "greet")
        {
            string name = await StringCodec.DecodePayloadStringAsync(request.Payload);
            Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");

            return new OutgoingResponse(request) { Payload = StringCodec.EncodeString($"Hello, {name}!") };
        }
        else
        {
            // We only implement greet.
            return new OutgoingResponse(request, StatusCode.NotImplemented);
        }
    }
}
```

[api]: https://api.icerpc.com/csharp/api/
[docs]:https://docs.icerpc.dev
[getting-started]: https://docs.icerpc.dev/getting-started
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[examples]: https://github.com/icerpc/icerpc-csharp/tree/main/examples
[package]: https://www.nuget.org/packages/IceRpc
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc
