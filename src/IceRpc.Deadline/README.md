# Deadline interceptor and middleware for IceRPC

IceRpc.Deadline provides an [IceRPC][icerpc-csharp] interceptor and the corresponding middleware.

The deadline interceptor adds deadline fields to outgoing requests, while the deadline middleware decodes deadline
fields received with incoming requests.

[Source code][source] | [Package][package] | [Example][example] | [API reference][api] | [Interceptor documentation][interceptor] | [Middleware documentation][middleware]

## Sample code

```csharp
// Client application

using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Add the deadline interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline()
    .UseDeadline(TimeSpan.FromSeconds(20))
    .Into(connection);
```

```csharp
// Server application

using IceRpc;

// Add the deadline middleware to the dispatch pipeline.
Router router = new Router()
    .UseDeadline();
    .Map<...>(...);

await using var server = new Server(router);
server.Listen();
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
        .AddIceRpcInvoker(builder =>
            builder
                // Add the deadline interceptor to the invocation pipeline.
               .UseDeadline(TimeSpan.FromSeconds(20))
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
                // Add the deadline middleware to the dispatch pipeline.
                .UseDeadline()
                .Map<...>()));

using var host = hostBuilder.Build();
host.Run();
```

[api]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Deadline.html
[example]: https://github.com/icerpc/icerpc-csharp/tree/0.1.x/examples/Deadline
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[interceptor]: https://docs.icerpc.dev/icerpc/invocation/interceptor
[middleware]: https://docs.icerpc.dev/icerpc/dispatch/middleware
[package]: https://www.nuget.org/packages/IceRpc.Deadline
[source]: https://github.com/icerpc/icerpc-csharp/tree/0.1.x/src/IceRpc.Deadline
