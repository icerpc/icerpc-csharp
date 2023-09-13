# Metrics interceptor and middleware for IceRPC

IceRpc.Metrics provides an [IceRPC][icerpc-csharp] interceptor and an IceRPC middleware.

The metrics interceptor instruments invocations using the [Meter API][meter], while the metrics middleware instruments
dispatches using the same API.

You can display the collected measurements with [dotnet-counters][dotnet_counters] and other tools.

[Source code][source] | [Package][package] | [Example][example] | [API reference][api] | [Interceptor documentation][interceptor] | [Middleware documentation][middleware]

## Sample code

```csharp
// Client application (C#)

using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Create an invocation pipeline and install the metrics interceptor.
Pipeline pipeline = new Pipeline().UseMetrics().Into(connection);
```

```csharp
// Server application (C#)

using IceRpc;

// Create a router (dispatch pipeline) and install the metrics middleware.
Router router = new Router().UseMetrics().Map<...>(...);
```

## Sample code with DI

```csharp
// Client application (C#)

using IceRpc;
using IceRpc.Extensions.DependencyInjection;

var hostBuilder = Host.CreateDefaultBuilder(args);

hostBuilder.ConfigureServices(services =>
    services
        .AddIceRpcClientConnection(new Uri("icerpc://localhost"))
        .AddIceRpcInvoker(builder =>
            builder
                // Add the metrics interceptor to the invocation pipeline.
               .UseMetrics()
               .Into<ClientConnection>()));

using var host = hostBuilder.Build();
host.Run();
```

```csharp
// Server application (C#)

using IceRpc;
using IceRpc.Extensions.DependencyInjection;

var hostBuilder = Host.CreateDefaultBuilder(args);

hostBuilder.ConfigureServices(services =>
    services
        .AddIceRpcServer(builder =>
            builder
                // Add the metrics middleware to the dispatch pipeline.
                .UseMetrics()
                .Map<...>()));

using var host = hostBuilder.Build();
host.Run();
```

[api]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Metrics.html
[dotnet_counters]: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[interceptor]: https://docs.icerpc.dev/icerpc/invocation/interceptor
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/Metrics
[meter]: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics.meter
[middleware]: https://docs.icerpc.dev/icerpc/dispatch/middleware
[package]: https://www.nuget.org/packages/IceRpc.Metrics
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Metrics
