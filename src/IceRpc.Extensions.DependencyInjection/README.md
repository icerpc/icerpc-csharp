# IceRPC extensions for Microsoft's DI container

IceRpc.Extensions.DependencyInjection helps you build applications with [IceRPC][icerpc-csharp] and
[Microsoft's DI container][ms_di].

[Source code][source] | [Package][package] | [Example][example] | [API reference][api] | [Product documentation][product]

## Sample code

```csharp
// Client application

using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var hostBuilder = Host.CreateDefaultBuilder(args);

hostBuilder.ConfigureServices(services =>
    services
        .AddIceRpcClientConnection(new Uri("icerpc://localhost"))
        // Builds an invocation pipeline singleton.
        .AddIceRpcInvoker(builder =>
            builder
               .UseDeadline(TimeSpan.FromSeconds(20))
               .UseLogger()
               .Into<ClientConnection>()));

using var host = hostBuilder.Build();
host.Run();
```

```csharp
// Server application

using IceRpc;
using IceRpc.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var hostBuilder = Host.CreateDefaultBuilder(args);

hostBuilder.ConfigureServices(services =>
    services
        // Add a server and configure its dispatch pipeline.
        .AddIceRpcServer(builder =>
            builder
                .UseDeadline()
                .UseLogger()
                .Map<...>()));

using var host = hostBuilder.Build();
host.Run();
```

[api]: https://api.testing.zeroc.com/csharp/api/IceRpc.Extensions.DependencyInjection.html
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/GenericHost
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[ms_di]: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection
[package]: https://www.nuget.org/packages/IceRpc.Extensions.DependencyInjection
[product]: https://docs.testing.zeroc.com/docs/icerpc-core/dependency-injection/di-and-icerpc-for-csharp
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Extensions.DependencyInjection
