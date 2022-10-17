// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

// Establish the connection to the server
await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

// Setup the invocation pipeline with the deflate interceptor
IInvoker pipeline = new Pipeline().UseDeflate().Into(connection);

// Create the proxy using the invocation pipeline
var hello = new HelloProxy(pipeline);

string greeting = await hello.SayHelloAsync(Environment.UserName);

Console.WriteLine(greeting);
