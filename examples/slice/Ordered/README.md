# Ordered

This example application illustrates that separate oneway requests can be delivered and dispatched in any order, and
how to use a stream to guarantee that the server receives and processes a series of elements in the order the client
writes them.

The client sends the same ten log entries to the server twice:

1. With `SimpleLogger`, each log entry is a separate oneway request. A oneway invocation completes as soon as the
   request is sent, and the server dispatches each request as it arrives, concurrently with the others. A dispatch can
   overtake another, so the server can log the entries out of order. To make this more visible, `Scribe`, the server's
   implementation of `SimpleLogger`, waits for a brief random delay before logging an entry.

   With QUIC, a request can also overtake another during transmission: each request travels in its own QUIC stream,
   and the network can deliver the packets of these streams in any order. This example does not demonstrate this
   reordering, which is unlikely on a local network, but it is another reason not to rely on the dispatch order of
   separate requests.

2. With `StreamLogger`, the client sends all the log entries in a single request, as a stream. The elements of a stream
   are delivered in the order the client writes them, so `Chronicler`, the server's implementation of `StreamLogger`,
   always logs the entries in order, even with the same brief random delay before logging each entry.

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
