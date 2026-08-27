# Greeter

The Greeter example illustrates how to send a request and wait for the response.

This example uses QUIC, IceRPC's default multiplexed transport. On Linux and macOS, QUIC requires extra setup
steps; see .NET's [QUIC platform dependencies][quic-platform].

You can build the client and server applications with:

``` shell
dotnet build
```

First start the Server program:

```shell
cd Server
dotnet run
```

In a separate terminal, start the Client program:

```shell
cd Client
dotnet run
```

> This is a concise example with minimal code. If you are looking for a more realistic example, see the
> [Logger](../Logger/README.md) example.

[quic-platform]: https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/quic/quic-overview#platform-dependencies
