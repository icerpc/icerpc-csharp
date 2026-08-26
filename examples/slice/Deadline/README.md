# Deadline

The Deadline example illustrates how to use the deadline interceptor to add an invocation deadline and shows
how invocations that exceed the deadline fail with TimeoutException. It also demonstrates how the [IDeadlineFeature]
can be used to set the deadline for an invocation.

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

[IDeadlineFeature]: https://code.icerpc.dev/csharp/main/api/reference/IceRpc.Features.IDeadlineFeature.html
[quic-platform]: https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/quic/quic-overview#platform-dependencies
