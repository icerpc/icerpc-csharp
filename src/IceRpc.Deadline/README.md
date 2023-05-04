# Deadline Interceptor and Middleware for IceRPC

IceRpc.Deadline provides an [IceRPC][icerpc] interceptor that adds deadline fields to the requests you send.

In addition, IceRpc.Deadline provides a middleware that decodes the deadline fields received with incoming requests.

[Source code][source] | [Package][package] | [Example][example] | [API reference documentation][api] | [Interceptor documentation][interceptor] | [Middleware documentation][middleware]

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

[api]: https://api.testing.zeroc.com/csharp/api/IceRpc.Deadline.html
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/Deadline
[icerpc]: https://www.nuget.org/packages/IceRpc
[interceptor]: https://docs.testing.zeroc.com/docs/icerpc-core/invocation/interceptor
[middleware]: https://docs.testing.zeroc.com/docs/icerpc-core/dispatch/middleware
[package]: https://www.nuget.org/packages/IceRpc.Deadline
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Deadline
