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

This sample stays focused on the retry registration. For a complete Generic Host setup with certificates and
configuration, see the [GenericHost example].

```csharp
// Client application

using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder(args);

hostBuilder.Services
    .AddIceRpcConnectionCache()
    .AddIceRpcInvoker(builder =>
        builder
            // Add the retry interceptor to the invocation pipeline.
            .UseRetry()
            .Into<ConnectionCache>());

using IHost host = hostBuilder.Build();
host.Run();
```

[api]: https://code.icerpc.dev/csharp/main/api/reference/IceRpc.Retry.html
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[interceptor]: https://docs.icerpc.dev/icerpc/invocation/interceptor
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/slice/Retry
[package]: https://www.nuget.org/packages/IceRpc.Retry
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Retry
[GenericHost example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/slice/GenericHost
