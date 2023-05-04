# Logger Interceptor and Middleware for IceRPC

IceRpc.Logger provides an [IceRPC][icerpc] interceptor that logs every invocations and an IceRPC middleware that logs
every dispatches.

[Source code][source] | [Package][package] | [Example][example] | [API reference documentation][api] | [Interceptor documentation][interceptor] | [Middleware documentation][middleware]

## Sample code

```csharp
// Client application

using Microsoft.Extensions.Logging;
using IceRpc;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(
    builder => builder.AddSimpleConsole().AddFilter("IceRpc", LogLevel.Debug));

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Create an invocation pipeline and install the logger interceptor. This interceptor logs
// invocations using category `IceRpc.Logger.LoggerInterceptor`.
Pipeline pipeline = new Pipeline().UseLogger(loggerFactory).Into(connection);
```

```csharp
// Server application

using Microsoft.Extensions.Logging;
using IceRpc;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(
    builder => builder.AddSimpleConsole().AddFilter("IceRpc", LogLevel.Debug));

// Create a router (dispatch pipeline) and install the logger middleware. This middleware logs
// dispatches using category `IceRpc.Logger.LoggerMiddleware`.
Router router = new Router().UseLogger(loggerFactory).Map<...>(...);
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
                // Add the logger interceptor to the invocation pipeline.
               .UseLogger()
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
                // Add the logger middleware to the dispatch pipeline.
                .UseLogger()
                .Map<...>()));

using var host = hostBuilder.Build();
host.Run();
```

[api]: https://api.testing.zeroc.com/csharp/api/IceRpc.Logger.html
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/GreeterLog
[interceptor]: https://docs.testing.zeroc.com/docs/icerpc-core/invocation/interceptor
[icerpc]: https://www.nuget.org/packages/IceRpc
[middleware]: https://docs.testing.zeroc.com/docs/icerpc-core/dispatch/middleware
[package]: https://www.nuget.org/packages/IceRpc.Logger
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Logger
