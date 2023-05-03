# GreeterLog

This example augments [Greeter](../Greeter/README.md) with logging.

The client is a more typical client than Greeter's client because it creates an invocation pipeline and installs an
interceptor in this pipeline (the logger interceptor).

And the server is a more typical server than Greeter's server because it creates a dispatch pipeline (a Router) and
installs a middleware in this pipeline (the logger middleware).

You can build the client and server applications with:

``` shell
dotnet build
```

First start the Server program:

```shell
dotnet run --project Server/Server.csproj
```

In a separate window, start the Client program:

```shell
dotnet run --project Client/Client.csproj
```

You will notice the two logger categories in the output. The client shows log messages for `IceRpc.ClientConnection` and
`IceRpc.Logger.LoggerInterceptor`, while the server shows log messages for `IceRpc.Server` and
`IceRpc.Logger.LoggerMiddleware`.
