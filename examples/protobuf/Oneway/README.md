# Oneway

This example application illustrates how to send oneway requests with the IceRPC + Protobuf integration, and what you
give up by doing so.

A two-way invocation completes when the client receives the response, after the server has dispatched the request. A
oneway invocation completes as soon as the request is sent: the client doesn't wait for the server to dispatch the
request, and never finds out whether the server dispatched it at all. Oneway requests suit best-effort traffic such as
logging and telemetry.

Protobuf provides no way to mark an RPC as oneway. With IceRPC, oneway is a property of the request: the client marks
a request as oneway by setting `IsOneway` on this request, which this example does with an interceptor. IceRPC
completes a oneway invocation with a response that has an empty payload, and the generated client decodes this empty
payload into a default-constructed response message. This works with any response message type; this example uses
`google.protobuf.Empty` since the client has no use for the response.

The client sends the same ten log entries to the server twice, each entry in its own request, and prints how long each
run takes:

1. With two-way requests. Each invocation waits for the server to log the entry, so the run takes at least ten times
   the processing time of `Scribe`, the server's implementation of `SimpleLogger`.

2. With oneway requests. Each invocation completes as soon as the request is sent, so the run completes in a few
   milliseconds, before the server logs any of the entries.

Each log entry carries the time the client created it, which `Scribe` prints with the entry. In the two-way run, these
times are spaced by the processing time of `Scribe`; in the oneway run, they are all within a few milliseconds.

The server dispatches the requests it receives concurrently, so in the oneway run, the server may log the entries out
of order. See the [Ordered](../../slice/Ordered/) Slice example for a discussion of this behavior.

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

[quic-platform]: https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/quic/quic-overview#platform-dependencies
