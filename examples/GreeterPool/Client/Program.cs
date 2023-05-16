// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Slice;
using System.Buffers;
using System.IO.Pipelines;
using VisitorCenter;

// Create a list of connections to our servers.
var connectionList = new List<ClientConnection>();
for (int port = 10000; port < 10003; ++port)
{
    connectionList.Add(new ClientConnection(new Uri($"icerpc://localhost:{port}")));
}

try
{
    var proxies = connectionList.Select(connection => new GreeterProxy(connection));

    // First the easy way to send all the requests in parallel.
    string[] greetings = await Task.WhenAll(proxies.Select(proxy => proxy.GreetAsync(Environment.UserName)));
    Array.ForEach(greetings, Console.WriteLine);
    Console.WriteLine("---");

    // We do the same a second time, except we encode the argument (name) only once as an optimization, and we take
    // advantage of the generated Request and Response nested classes.
    // For this example, this optimization is naturally overkill.

    PipeReader payload = GreeterProxy.Request.Greet(Environment.UserName);
    _ = payload.TryRead(out ReadResult readResult);
    ReadOnlySequence<byte> buffer = readResult.Buffer;

    greetings = await Task.WhenAll(proxies.Select(proxy =>
        proxy.InvokeAsync(
            operation: "greet",
            payload: PipeReader.Create(buffer), // each request needs its own payload
            payloadContinuation: null,
            responseDecodeFunc: GreeterProxy.Response.GreetAsync)));

    Array.ForEach(greetings, Console.WriteLine);

    // Cleanup payload
    payload.Complete();

    // Shutdown
    await Task.WhenAll(connectionList.Select(connection => connection.ShutdownAsync()));
}
finally
{
    await Task.WhenAll(connectionList.Select(connection => connection.DisposeAsync().AsTask()));
}
