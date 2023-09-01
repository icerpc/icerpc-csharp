# RequestContext interceptor and middleware for IceRPC

IceRpc.RequestContext provides an [IceRPC][icerpc-csharp] interceptor that encodes request context features into request
context fields. It also provides the corresponding middleware that decodes request context fields into request context
features.

[Source code][source] | [Package][package] | [Example][example] | [API reference][api] | [Interceptor documentation][interceptor] | [Middleware documentation][middleware]

## Sample code

```csharp
// Client application

using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Add the request context interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline()
    .UseRequestContext()
    .Into(connection);
```

```csharp
// Server application

using IceRpc;

// Add the request context middleware to the dispatch pipeline.
Router router = new Router()
    .UseRequestContext()
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
                // Add the request context interceptor to the invocation pipeline.
               .UseRequestContext()
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
                // Add the request context middleware to the dispatch pipeline.
                .UseRequestContext()
                .Map<...>()));

using var host = hostBuilder.Build();
host.Run();
```

## Ice interop

The ice protocol can send and receive request context fields. They correspond to
[request contexts][ice_request_contexts] in Ice.

[api]: https://api.testing.zeroc.com/csharp/api/IceRpc.RequestContext.html
[ice_request_contexts]: https://doc.zeroc.com/ice/3.7/client-side-features/request-contexts
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[interceptor]: https://docs.testing.zeroc.com/icerpc/invocation/interceptor
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/RequestContext
[middleware]: https://docs.testing.zeroc.com/icerpc/dispatch/middleware
[package]: https://www.nuget.org/packages/IceRpc.RequestContext
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.RequestContext
