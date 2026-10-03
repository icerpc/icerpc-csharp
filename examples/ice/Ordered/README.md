# Ordered

This example application illustrates how to dispatch the requests received over an `ice` connection one at a time,
in the order the client sends them, by setting `MaxDispatches` to `1` in the server's connection options.

The client sends ten log entries to the server, each in its own oneway request. A oneway invocation completes as soon
as the request is sent, without waiting for the server to dispatch it. With the `ice` protocol, the requests are
written to the connection in order, and the server reads them in the same order: a request cannot overtake another
during transmission. It can only overtake another during dispatch, since by default the server dispatches the requests
it reads concurrently.

`Scribe`, the server's implementation of `SimpleLogger`, waits for a brief random delay before logging an entry, to
simulate the variable processing time of a real logger. With `MaxDispatches = 1`, the server doesn't start a dispatch
until the previous one completes, so this delay doesn't change the order of the entries: the server always logs them
in order. To see the entries logged out of order, comment out `MaxDispatches = 1` in the server's `Program.cs`, then
rebuild and rerun the server.

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
