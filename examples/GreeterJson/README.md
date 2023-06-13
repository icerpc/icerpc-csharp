# GreeterJson

The GreeterJson example illustrates how to use Json to encode the payloads of IceRPC requests and
responses.

It's a variation of the [Greeter](Greeter) example that uses Json instead of Slice. See also the
[GreeterCore](GreeterCore) example that uses the same core APIs to create requests and responses.

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
