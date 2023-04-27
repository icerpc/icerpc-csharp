# Compressor Interceptor and Middleware for IceRPC

IceRpc.Compressor provides an [IceRPC][icerpc] interceptor that allows you to compress the payloads of the requests
you send. This interceptor can also decompress the payloads of the responses you receive.

In addition, IceRpc.Compressor provides an IceRPC middleware that allows you to compress the responses you send back. This
middleware can also decompress the payloads of the requests you receive.

[Source code][source] | [Package][package] | [Example][example] | [API reference documentation][api] | [Interceptor documentation][interceptor] | [Middleware documentation][middleware]

## Sample code

```slice
// Slice definitions

interface Greeter {
    // The Compress attribute works with the Compressor interceptor and middleware.
    // Without this attribute, the request and response payloads are not compressed or decompressed.
    [compress(Args, Return)] greet(name: string) -> string
}
```

```csharp
// Server application

using IceRpc;

// Add the compressor middleware to the dispatch pipeline.
Router router = new Router()
    .UseCompressor(CompressionFormat.Brotli);

router.Map<...>(...);

await using var server = new Server(router);
server.Listen();
```

```csharp
// Client application

using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Add the compressor interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline()
    .UseCompressor(CompressionFormat.Brotli)
    .Into(connection);
```

## Remarks

The Compressor interceptor and middleware compress and decompress payloads regardless of how
these payloads are encoded. They work well with Slice but don't depend on Slice.

[api]: https://api.testing.zeroc.com/csharp/api/IceRpc.Compressor.html
[interceptor]: https://docs.testing.zeroc.com/docs/icerpc-core/invocation/interceptor
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/Compress
[icerpc]: https://www.nuget.org/packages/IceRpc
[middleware]: https://docs.testing.zeroc.com/docs/icerpc-core/dispatch/middleware
[package]: https://www.nuget.org/packages/IceRpc.Compressor 
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Compressor
