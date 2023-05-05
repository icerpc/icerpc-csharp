# Metrics

This example application illustrates how to use the metrics interceptor and middleware, and how to use
`dotnet-counters` to monitor the client invocations metrics and the server dispatches metrics.

To collect counter metrics, you need to install the `dotnet-counters` tools.

https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters

You can build the client and server applications with:

``` shell
dotnet build
```

First start the Server program:

```shell
dotnet run --project Server/Server.csproj
```

To monitor the server counter metrics, in a separate window run:

```shell
dotnet-counters monitor --name Server --counters IceRpc.Dispatch
```

In a separate window run the client program to send requests to the server:

```shell
dotnet run --project Client/Client.csproj
```

To monitor the client counter metrics, in a separate window run:

```shell
dotnet-counters monitor --name Client --counters IceRpc.Invocation
```
