# Logger interceptor and middleware for IceRPC

IceRpc.Logger provides an [IceRPC][icerpc-csharp] interceptor that logs every invocation and an IceRPC middleware that
logs every dispatch.

[Source code][source] | [Package][package] | [Example][example] | [API reference][api] | [Interceptor documentation][interceptor] | [Middleware documentation][middleware]

## Sample code

```csharp
// Client application

using Microsoft.Extensions.Logging;
using IceRpc;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Debug));

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Create an invocation pipeline and install the logger interceptor. This interceptor logs
// invocations using category `IceRpc.Logger.LoggerInterceptor`.
Pipeline pipeline = new Pipeline()
    .UseLogger(loggerFactory)
    .Into(connection);
```

```csharp
// Server application

using Microsoft.Extensions.Logging;
using IceRpc;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Debug));

// Create a router (dispatch pipeline) and install the logger middleware. This middleware
// logs dispatches using category `IceRpc.Logger.LoggerMiddleware`.
Router router = new Router()
    .UseLogger(loggerFactory)
    .Map<...>(...);
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
            // Add the logger interceptor to the invocation pipeline.
            builder
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
            // Add the logger middleware to the dispatch pipeline.
            builder
                .UseLogger()
                .Map<...>()));

using var host = hostBuilder.Build();
host.Run();
```

[api]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Logger.html
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/GreeterLog
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[interceptor]: https://docs.icerpc.dev/icerpc/invocation/interceptor
[middleware]: https://docs.icerpc.dev/icerpc/dispatch/middleware
[package]: https://www.nuget.org/packages/IceRpc.Logger
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Logger
