This application illustrates how to use the RequestContext interceptor to encode the request context into a field, and
decode it using the RequestCotnext middleware. The request context is a colection of string key value pairs encoded in
a request header field.

For build instructions check the top-level [README.md](../../README.md).

First start the Server program:

```
dotnet run --project Server/Server.csproj
```

In a separate window, start the Client program:

```
dotnet run --project Client/Client.csproj
```
