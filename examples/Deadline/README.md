# Deadline

The Deadline example illustrates how to use the deadline interceptor to add an invocation deadline and shows
how invocations that exceed the deadline fail with TimeoutException, it also demonstrates how the IDealineFeature
can be used to set the deadline for an invocation.

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
