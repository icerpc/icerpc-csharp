# Telemetry interceptor and middleware for IceRPC

IceRpc.Telemetry provides an [IceRPC][icerpc-csharp] interceptor and the corresponding middleware.

The telemetry interceptor starts an activity per request, following [OpenTelemetry][open-telemetry] conventions. The
activity context is propagated with the request using the trace context request field. The telemetry middleware starts
an activity per dispatch. When the trace context request field is present in the incoming request the telemetry
middleware restores the activity context and uses it to set the parent activity of the dispatch activity it starts.

The telemetry interceptor and middleware create activities only for requests that use the `icerpc` protocol; they
don't create activities for requests that use the `ice` protocol.

[Source code][source] | [Package][package] | [Example][example] | [API reference][api] | [Interceptor documentation][interceptor] | [Middleware documentation][middleware]

## Activity name and tags

The name of an activity is `{path}/{operation}`, for example `/greeter/greet`. The interceptor and the middleware set
the following tags, named after the OpenTelemetry [semantic conventions for RPC spans][rpc-spans]:

| Tag                    | Client activity (interceptor)                                                              | Server activity (middleware)                 | Example                                      |
|------------------------|--------------------------------------------------------------------------------------------|----------------------------------------------|----------------------------------------------|
| `rpc.system.name`      | `icerpc`                                                                                   | `icerpc`                                     | `icerpc`                                     |
| `rpc.method`           | The operation name.                                                                        | The operation name.                          | `greet`                                      |
| `icerpc.path`          | The path of the service address.                                                           | The path carried by the request.             | `/greeter`                                   |
| `rpc.status_code`      | The status code carried by the response.                                                   | The status code sent to the caller.          | `Ok`, `DeadlineExceeded`                     |
| `error.type`           | A status code other than `Ok`, or the full name of the exception thrown by the invocation. | A status code that reports a server failure. | `DeadlineExceeded`, `IceRpc.IceRpcException` |
| `network.peer.address` | The IP address of the server.                                                              | The IP address of the client.                | `10.1.2.80`                                  |
| `network.peer.port`    | The port of the server.                                                                    | The port of the client.                      | `4062`                                       |
| `icerpc.canceled`      | `true` when the invocation was canceled.                                                   | `true` when the dispatch was canceled.       | `true`                                       |

An activity with an `error.type` tag has the `Error` status; the status of any other activity is unset. For the
middleware, the `ApplicationError`, `NotFound`, `InvalidData`, `TruncatedPayload` and `Unauthorized` status codes report
a problem with the request, not a server failure.

## Sample code

```csharp
// Client application

using IceRpc;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

// The activity source used by the telemetry interceptor.
using var activitySource = new ActivitySource("IceRpc");

// Load the test root CA certificate in order to connect to the server that uses a test
// server certificate.
using var rootCA = X509CertificateLoader.LoadCertificateFromFile("certs/cacert.der");

await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    // examples/common/Program.Authentication.cs in the icerpc-csharp repo provides the
    // CreateClientAuthenticationOptions helper method
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

// Add the telemetry interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline().UseTelemetry(activitySource).Into(connection);
```

```csharp
// Server application

using IceRpc;
using System.Diagnostics;

// The activity source used by the telemetry interceptor and middleware.
using var activitySource = new ActivitySource("IceRpc");

// Add the telemetry middleware to the dispatch pipeline.
Router router = new Router().UseTelemetry(activitySource);
```

## Sample code with DI

This sample stays focused on the telemetry registration. For a complete Generic Host setup with certificates and
configuration, see the [GenericHost example].

```csharp
// Client application

using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder(args);

hostBuilder.Services
    .AddIceRpcClientConnection(new Uri("icerpc://localhost"))
    // The activity source used by the telemetry interceptor.
    .AddSingleton(_ => new ActivitySource("IceRpc"))
    .AddIceRpcInvoker(builder =>
        builder
            // Add the telemetry interceptor to the invocation pipeline.
            .UseTelemetry()
            .Into<ClientConnection>());

using IHost host = hostBuilder.Build();
host.Run();
```

```csharp
// Server application

using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder(args);

hostBuilder.Services
    // The activity source used by the telemetry middleware.
    .AddSingleton(_ => new ActivitySource("IceRpc"))
    .AddIceRpcServer(builder =>
        builder
            // Add the telemetry middleware to the dispatch pipeline.
            .UseTelemetry()
            .Map<...>());

using IHost host = hostBuilder.Build();
host.Run();
```

[api]: https://code.icerpc.dev/csharp/main/api/reference/IceRpc.Telemetry.html
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/slice/Telemetry
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[interceptor]: https://docs.icerpc.dev/icerpc/invocation/interceptor
[middleware]: https://docs.icerpc.dev/icerpc/dispatch/middleware
[package]: https://www.nuget.org/packages/IceRpc.Telemetry
[open-telemetry]: https://opentelemetry.io/
[rpc-spans]: https://opentelemetry.io/docs/specs/semconv/rpc/rpc-spans/
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Telemetry
[GenericHost example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/slice/GenericHost
