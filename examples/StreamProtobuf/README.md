# StreamProtobuf

This example application illustrates how to stream random numbers from a server to a client. This is a variation
of the [Stream] example but using [Protobuf] instead of Slice to define the contract between the client and the server.

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

[Stream]: ./Stream
[Protobuf]: https://protobuf.dev/getting-started/csharptutorial/
