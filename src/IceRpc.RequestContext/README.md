# RequestContext interceptor and middleware for IceRPC

IceRpc.RequestContext provides an [IceRPC][icerpc-csharp] interceptor that encodes request context features into request
context fields. It also provides the corresponding middleware that decodes request context fields into request context
features.

[Source code][source] | [Package][package] | [Example][example] | [API reference][api] | [Interceptor documentation][interceptor] | [Middleware documentation][middleware]

## Sample code

```csharp
// Client application

using IceRpc;
using System.Security.Cryptography.X509Certificates;

// Load the test root CA certificate in order to connect to the server that uses a test
// server certificate.
using var rootCA = X509CertificateLoader.LoadCertificateFromFile("certs/cacert.der");

await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    // examples/common/Program.Authentication.cs in the icerpc-csharp repo provides the
    // CreateClientAuthenticationOptions helper method
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

// Add the request context interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline()
    .UseRequestContext()
    .Into(connection);
```

```csharp
// Server application

using IceRpc;
using System.Security.Cryptography.X509Certificates;

// Add the request context middleware to the dispatch pipeline.
Router router = new Router()
    .UseRequestContext()
    .Map(...);

// The default transport (QUIC) requires a server certificate.
// We use a test certificate here.
using var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

await using var server = new Server(
    router,
    // examples/common/Program.Authentication.cs in the icerpc-csharp repo provides the
    // CreateServerAuthenticationOptions helper method
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate));
server.Listen();
```

## Sample code with DI

This sample stays focused on the request context registration. For a complete Generic Host setup with certificates and
configuration, see the [GenericHost example].

```csharp
// Client application

using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder(args);

hostBuilder.Services
    .AddIceRpcClientConnection(new Uri("icerpc://localhost"))
    .AddIceRpcInvoker(builder =>
        builder
            // Add the request context interceptor to the invocation pipeline.
            .UseRequestContext()
            .Into<ClientConnection>());

using IHost host = hostBuilder.Build();
host.Run();
```

```csharp
// Server application

using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder(args);

hostBuilder.Services
    .AddIceRpcServer(builder =>
        builder
            // Add the request context middleware to the dispatch pipeline.
            .UseRequestContext()
            .Map<...>());

using IHost host = hostBuilder.Build();
host.Run();
```

## Ice interop

The ice protocol can send and receive request context fields. They correspond to
[request contexts][ice_request_contexts] in Ice.

[api]: https://code.icerpc.dev/csharp/main/api/reference/IceRpc.RequestContext.html
[ice_request_contexts]: https://docs.zeroc.com/ice/3.8/csharp/request-contexts
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[interceptor]: https://docs.icerpc.dev/icerpc/invocation/interceptor
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/slice/RequestContext
[middleware]: https://docs.icerpc.dev/icerpc/dispatch/middleware
[package]: https://www.nuget.org/packages/IceRpc.RequestContext
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.RequestContext
[GenericHost example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/slice/GenericHost
