# RequestContext Interceptor and Middleware for IceRPC

IceRpc.RequestContext provides an [IceRPC][icerpc] interceptor that encodes request context features into request
context fields. It also provides the corresponding middleware that decodes request context fields into request context
features.

[Source code][source] | [Package][package] | [Example][example] | [API reference documentation][api] | [Interceptor documentation][interceptor] | [Middleware documentation][middleware]

## Sample code

```csharp
// Client application

using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Add the request context interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline()
    .UseRequestContext())
    .Into(connection);
```

```csharp
// Server application

using IceRpc;

// Add the request context middleware to the dispatch pipeline.
Router router = new Router()
    .UseRequestContext();

router.Map<...>(...);

await using var server = new Server(router);
server.Listen();
```

## Ice interop

The ice protocol can send and receive request context fields. See also [Ice request contexts][ice_request_contexts].

[api]: https://api.testing.zeroc.com/csharp/api/IceRpc.RequestContext.html
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/RequestContext
[ice_request_contexts]: https://doc.zeroc.com/ice/3.7/client-side-features/request-contexts
[interceptor]: https://docs.testing.zeroc.com/docs/icerpc-core/invocation/interceptor
[icerpc]: https://www.nuget.org/packages/IceRpc
[middleware]: https://docs.testing.zeroc.com/docs/icerpc-core/dispatch/middleware
[package]: https://www.nuget.org/packages/IceRpc.RequestContext
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.RequestContext
