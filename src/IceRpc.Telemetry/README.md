# Telemetry interceptor and middleware for IceRPC

IceRpc.Telemetry provides an [IceRPC][icerpc-csharp] interceptor and the corresponding middleware.

The telemetry interceptor starts an activity per request, following [OpenTelemetry][open-telemetry] conventions. The
activity context is propagated with the request using the trace context request field. The telemetry middleware starts
an activity per dispatch. When the trace context request field is present in the incoming request the telemetry
middleware restores the activity context and uses it to set the parent activity of the dispatch activity it starts.

[Source code][source] | [Package][package] | [Example][example] | [API reference][api] | [Interceptor documentation][interceptor] | [Middleware documentation][middleware]

## Sample code

```csharp
// Client application

using IceRpc;

// The activity source used by the telemetry interceptor.
using var activitySource = new ActivitySource("IceRpc");

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Add the deadline interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline().UseTelemetry(activitySource).Into(connection);
```

```csharp
// Server application

using IceRpc;
using VisitorCenter;

// The activity source used by the telemetry interceptor and middleware.
using var activitySource = new ActivitySource("IceRpc");

// Add the telemetry middleware to the dispatch pipeline.
Router router = new Router().UseTelemetry(activitySource);
```

## Sample code with DI

```csharp
// Client application

using IceRpc;
using IceRpc.Extensions.DependencyInjection;

var hostBuilder = Host.CreateDefaultBuilder(args);

hostBuilder.ConfigureServices(services =>
    services
        .AddIceRpcClientConnection(new Uri("icerpc://localhost"))
        // The activity source used by the telemetry interceptor.
        .AddSingleton(_ => new ActivitySource("IceRpc"))
        .AddIceRpcInvoker(builder =>
            builder
                // Add the telemetry interceptor to the invocation pipeline.
               .UseTelemetry()
               .Into<ClientConnection>()));

using var host = hostBuilder.Build();
host.Run();
```

```csharp
// Server application

using IceRpc;
using IceRpc.Extensions.DependencyInjection;

var hostBuilder = Host.CreateDefaultBuilder(args);

hostBuilder.ConfigureServices(services =>
    services
        .AddIceRpcServer(builder =>
            builder
                // The activity source used by the telemetry middleware.
                .AddSingleton(_ => new ActivitySource("IceRpc"))
                // Add the telemetry middleware to the dispatch pipeline.
                .UseTelemetry()
                .Map<...>()));

using var host = hostBuilder.Build();
host.Run();
```

[api]: https://api.testing.zeroc.com/csharp/api/IceRpc.Telemetry.html
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/Telemetry
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[interceptor]: https://docs.testing.zeroc.com/icerpc-core/invocation/interceptor
[middleware]: https://docs.testing.zeroc.com/icerpc-core/dispatch/middleware
[package]: https://www.nuget.org/packages/IceRpc.Telemetry
[open-telemetry]: https://opentelemetry.io/
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Telemetry
