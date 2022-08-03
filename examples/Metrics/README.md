This example application illustrates how to use the metrics middleware with `dotnet-counters` and `dotnet-trace` to
monitor the requests dispatched by a server.

For build instructions check the top-level [README.md](../../README.md).
To collect trace and counter metrics, install the `dotnet-counters` and `dotnet-trace` tools.

First start the Server program:

```shell
dotnet run --project Server/Server.csproj
```

To monitor the counter metrics, in a separate window run:

```shell
dotnet-counters monitor --name Server --counters IceRpc.Dispatch
```

To collect the trace metrics, in a separate window run:

```shell
dotnet-trace collect --name Server --providers IceRpc.Dispatch
```

In a separate window run the client program to send requests to the server:

```shell
dotnet run --project Client/Client.csproj
```
