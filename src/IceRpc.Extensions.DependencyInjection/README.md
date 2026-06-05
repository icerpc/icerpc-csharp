# IceRPC extensions for Microsoft's DI container

IceRpc.Extensions.DependencyInjection helps you build applications with [IceRPC][icerpc-csharp] and
[Microsoft's DI container][ms_di].

[Source code][source] | [Package][package] | [Example][example] | [API reference][api] | [Product documentation][product]

## Sample code

This sample shows only the basic DI wiring. For a complete Generic Host setup with certificates and configuration, see
the [GenericHost example].

```csharp
// Client application

using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder(args);

hostBuilder.Services
    .AddIceRpcClientConnection(new Uri("icerpc://localhost"))
    // Builds an invocation pipeline singleton.
    .AddIceRpcInvoker(builder =>
        builder
           .UseDeadline(TimeSpan.FromSeconds(20))
           .UseLogger()
           .Into<ClientConnection>());

using IHost host = hostBuilder.Build();
host.Run();
```

```csharp
// Server application

using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder(args);

hostBuilder.Services
    // Add a server and configure its dispatch pipeline.
    .AddIceRpcServer(builder =>
        builder
            .UseDeadline()
            .UseLogger()
            .Map<...>());

using IHost host = hostBuilder.Build();
host.Run();
```

[api]: https://code.icerpc.dev/csharp/main/api/reference/IceRpc.Extensions.DependencyInjection.html
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/slice/GenericHost
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[ms_di]: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection
[package]: https://www.nuget.org/packages/IceRpc.Extensions.DependencyInjection
[product]: https://docs.icerpc.dev/icerpc/dependency-injection/di-and-icerpc-for-csharp
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Extensions.DependencyInjection
[GenericHost example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/slice/GenericHost
