# RequestContext

This application illustrates how to use the RequestContext interceptor to encode the request context into a field, and
decode it using the RequestContext middleware. The request context is a collection of string key value pairs encoded in
a request header field.

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
