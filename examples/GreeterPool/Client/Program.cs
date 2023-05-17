// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Slice;
using System.Buffers;
using System.IO.Pipelines;
using VisitorCenter;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

var proxies = new List<GreeterProxy>
{
    new GreeterProxy(connection, new Uri("icerpc:/greeter/english")),
    new GreeterProxy(connection, new Uri("icerpc:/greeter/french")),
    new GreeterProxy(connection, new Uri("icerpc:/greeter/spanish"))
};

// First the easy way to send all the requests in parallel.
string[] greetings = await Task.WhenAll(from proxy in proxies select proxy.GreetAsync(Environment.UserName));
Array.ForEach(greetings, Console.WriteLine);
Console.WriteLine("---");

// We do the same a second time, except we encode the argument (name) only once as an optimization, and we take
// advantage of the generated Request and Response nested classes.
// For this example, this optimization is naturally overkill.

PipeReader payload = GreeterProxy.Request.Greet(Environment.UserName);
_ = payload.TryRead(out ReadResult readResult);
ReadOnlySequence<byte> buffer = readResult.Buffer;

greetings = await Task.WhenAll(
    from proxy in proxies
    select proxy.InvokeAsync(
            operation: "greet",
            payload: PipeReader.Create(buffer), // each request needs its own payload
            payloadContinuation: null,
            responseDecodeFunc: GreeterProxy.Response.GreetAsync));

Array.ForEach(greetings, Console.WriteLine);

// Cleanup payload
payload.Complete();

await connection.ShutdownAsync();
