# RequestContext

This example illustrates how to use the request context interceptor to encode request context features into request
context fields. It also shows how to use the request context middleware to decode request context fields into request
context features.

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
