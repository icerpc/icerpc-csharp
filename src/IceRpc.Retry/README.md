# Retry interceptor for IceRPC

IceRpc.Retry provides an [IceRPC][icerpc-csharp] interceptor that retries failed invocations automatically.

[Source code][source] | [Package][package] | [Example][example] | [API reference][api] | [Interceptor documentation][interceptor]

## Sample code

```csharp
// Client application

using IceRpc;

await using var connectionCache = new ConnectionCache();

// Create an invocation pipeline and install the retry interceptor.
Pipeline pipeline = new Pipeline()
    .UseRetry()
    .Into(connectionCache);
```

## Sample code with DI

```csharp
// Client application

using IceRpc;
using IceRpc.Extensions.DependencyInjection;

var hostBuilder = Host.CreateDefaultBuilder(args);

hostBuilder.ConfigureServices(services =>
    services
        .AddIceRpcConnectionCache()
        .AddIceRpcInvoker(builder =>
            builder
                // Add the retry interceptor to the invocation pipeline.
               .UseRetry()
               .Into<ConnectionCache>()));

using var host = hostBuilder.Build();
host.Run();
```

[api]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Retry.html
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[interceptor]: https://docs.icerpc.dev/icerpc/invocation/interceptor
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/Retry
[package]: https://www.nuget.org/packages/IceRpc.Retry
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Retry
