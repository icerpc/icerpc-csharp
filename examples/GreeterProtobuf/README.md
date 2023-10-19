# GreeterProtobuf

The GreeterProtobuf example is a variation of the [Greeter](Greeter) example. It sends the same request and receives
the same response, except it uses [Protobuf][protobuf] instead of Slice to define the contract between the client and
the server.

You can build the client and server applications with:

``` shell
dotnet build
```

First start the Server program:

```shell
cd Server
dotnet run
```

In a separate window, start the Client program:

```shell
cd Client
dotnet run
```

[protobuf]: https://protobuf.dev/getting-started/csharptutorial/
