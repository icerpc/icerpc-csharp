# HelloCore

Another "Hello World" example, this time implemented using the IceRPC core API--without Slice or generated code. It
illustrates how to send a request and wait for the response with the core API.

For build instructions check the top-level [README.md](../../README.md).

First start the Server program:

```shell
dotnet run --project Server/Server.csproj
```

In a separate window, start the Client program:

```shell
dotnet run --project Client/Client.csproj
```
