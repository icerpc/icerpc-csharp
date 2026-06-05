# IceRPC

IceRPC is a modular RPC framework that helps you build networked applications with minimal effort. The IceRpc assembly
and package represent the base assembly and package for the [C# implementation of IceRPC][icerpc-csharp].

[Package][package] | [Source code][source] | [Getting started][getting-started] | [Examples][examples] | [Documentation][docs] | [API reference][api]

## QUIC Transport

IceRPC's default transport is QUIC, a new UDP-based multiplexed transport used by HTTP/3 and other modern application
protocols.

IceRPC has the same platform dependencies as `System.Net.Quic`. See .NET's [QUIC Platform dependencies][platform].

## Sample Code

```csharp
// Client application

using IceRpc;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using VisitorCenter; // for GreetRequest and GreetResponse (see examples/json/Greeter/GreetRequest.cs and GreetResponse.cs)

// Load the test root CA certificate in order to connect to the server that uses a test
// server certificate.
using var rootCA = X509CertificateLoader.LoadCertificateFromFile("certs/cacert.der");

await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    // examples/common/Program.Authentication.cs in the icerpc-csharp repo provides the
    // CreateClientAuthenticationOptions helper method
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

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
    using var request = new OutgoingRequest(
        new ServiceAddress(new Uri("icerpc:/greeter")))
    {
        Operation = "greet",
        Payload = pipe.Reader // request takes ownership of the PipeReader
    };

    // Make the invocation: we send the request using the connection and then wait for the
    // response.
    IncomingResponse response = await connection.InvokeAsync(request);

    if (response.StatusCode == StatusCode.Ok)
    {
        // Deserialize the response payload.
        GreetResponse greeterResponse =
            await JsonSerializer.DeserializeAsync<GreetResponse>(response.Payload);

        // DeserializeAsync reads to completion but does not complete the PipeReader.
        response.Payload.Complete();

        return greeterResponse.Greeting;
    }
    else
    {
        // IceRPC guarantees the error message is non-null when StatusCode > Ok.
        Debug.Assert(response.ErrorMessage is not null);
        throw new DispatchException(response.StatusCode, response.ErrorMessage);
    }
}
```

```csharp
// Server application

using IceRpc;
using System.IO.Pipelines;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using VisitorCenter;

// The default transport (QUIC) requires a server certificate.
// We use a test certificate here.
using var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

// Create a server that dispatches all requests to the same service, an instance of
// Chatbot.
await using var server = new Server(
    new Chatbot(),
    // examples/common/Program.Authentication.cs in the icerpc-csharp repo provides the
    // CreateServerAuthenticationOptions helper method
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate));

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();

internal class Chatbot : IDispatcher
{
    public async ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Operation == "greet")
        {
            // Deserialize the request payload.
            GreetRequest greetRequest =
                await JsonSerializer.DeserializeAsync<GreetRequest>(
                    request.Payload,
                    cancellationToken: cancellationToken);

            // DeserializeAsync reads to completion but does not complete the PipeReader.
            request.Payload.Complete();

            Console.WriteLine(
                $"Dispatching Greet request {{ name = '{greetRequest.Name}' }}");

            // Create the greet response.
            var greetResponse = new GreetResponse
            {
                Greeting = $"Hello, {greetRequest.Name}!"
            };

            // Create a PipeReader holding the JSON response message.
            var pipe = new Pipe();
            await JsonSerializer.SerializeAsync(
                pipe.Writer,
                greetResponse,
                cancellationToken: cancellationToken);
            pipe.Writer.Complete();

            // Return the response.
            return new OutgoingResponse(request)
            {
                // the OutgoingResponse takes ownership of the PipeReader
                Payload = pipe.Reader
            };
        }
        else
        {
            // We only implement greet.
            return new OutgoingResponse(request, StatusCode.NotImplemented);
        }
    }
}
```

[api]: https://code.icerpc.dev/csharp/main/api/reference/IceRpc.html
[docs]:https://docs.icerpc.dev
[getting-started]: https://docs.icerpc.dev/getting-started
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[examples]: https://github.com/icerpc/icerpc-csharp/tree/main/examples
[package]: https://www.nuget.org/packages/IceRpc
[platform]: https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/quic/quic-overview#platform-dependencies
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc
