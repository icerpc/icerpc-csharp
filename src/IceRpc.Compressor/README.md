# Compressor Interceptor and Middleware for IceRPC

IceRpc.Compressor provides an [IceRPC][icerpc] interceptor that allows you to compress the payloads of the requests
you send. This interceptor decompresses the payloads of the responses you receive when these payloads are compressed.

In addition, IceRpc.Compressor provides an IceRPC middleware that allows you to compress the payloads of the responses
you send back. This middleware decompresses the payloads of the requests you receive when these payloads are compressed.

[Source code][source] | [Package][package] | [Example][example] | [API reference documentation][api] | [Interceptor documentation][interceptor] | [Middleware documentation][middleware]

## Sample code

```slice
// Slice definitions

interface Greeter {
    // The Compress attribute instructs the Compressor interceptor or middleware (if installed)
    // to compress the payload of the outgoing request or response. The Compressor interceptor
    // or middleware does not compress the payloads of Slice operations without this attribute.
    [compress(Args, Return)] greet(name: string) -> string
}
```

```csharp
// Server application

using IceRpc;

// Add the Compressor middleware to the dispatch pipeline.
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

// Add the Compressor interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline()
    .UseCompressor(CompressionFormat.Brotli)
    .Into(connection);
```

## Remarks

The Compressor interceptor and middleware compress and decompress payloads regardless of how
these payloads are encoded. They work well with Slice but don't require Slice.

[api]: https://api.testing.zeroc.com/csharp/api/IceRpc.Compressor.html
[interceptor]: https://docs.testing.zeroc.com/docs/icerpc-core/invocation/interceptor
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/Compress
[icerpc]: https://www.nuget.org/packages/IceRpc
[middleware]: https://docs.testing.zeroc.com/docs/icerpc-core/dispatch/middleware
[package]: https://www.nuget.org/packages/IceRpc.Compressor 
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Compressor
