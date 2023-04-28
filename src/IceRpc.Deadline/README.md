# Deadline Interceptor and Middleware for IceRPC

IceRpc.Deadline provides an [IceRPC][icerpc] interceptor that adds deadline fields to the requests you send.

In addition, IceRpc.Deadline provides a middleware that decodes and enforces the deadline fields received 
with incoming requests.

[Source code][source] | [Package][package] | [API reference documentation][api] | [Interceptor documentation][interceptor] | [Middleware documentation][middleware]

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
    
router.Map<...>(...);

await using var server = new Server(router);
server.Listen();
```

[api]: https://api.testing.zeroc.com/csharp/api/IceRpc.Deadline.html
[interceptor]: https://docs.testing.zeroc.com/docs/icerpc-core/invocation/interceptor
[icerpc]: https://www.nuget.org/packages/IceRpc
[middleware]: https://docs.testing.zeroc.com/docs/icerpc-core/dispatch/middleware
[package]: https://www.nuget.org/packages/IceRpc.Deadline 
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Deadline