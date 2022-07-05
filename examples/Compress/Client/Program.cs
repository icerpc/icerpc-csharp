// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

// Establish the connection to the server
await using var connection = new ClientConnection("icerpc://127.0.0.1");

// Setup the invocation pipeline with the deflate interceptor
IInvoker pipeline = new Pipeline().UseDeflate().Into(connection);

// Create the proxy using the invocation pipeline
var hello = new HelloProxy(pipeline);

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    Console.WriteLine(await hello.SayHelloAsync(name));
}
