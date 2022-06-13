// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

// Establish the connection to the server
await using var connection = new ClientConnection("icerpc://127.0.0.1");

// Setup the invocation pipeline with the deflate interceptor
IInvoker pipeline = new Pipeline().UseDeflate();

// Create the proxy using the connection and the invocation pipeline
IHelloPrx hello = HelloPrx.FromConnection(connection, null, pipeline);

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    Console.WriteLine(await hello.SayHelloAsync(name));
}
