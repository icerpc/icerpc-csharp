// Copyright (c) ZeroC, Inc.

using CompressExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Add the compressor interceptor to the invocation pipeline.
IInvoker pipeline = new Pipeline().UseCompressor(CompressionFormat.Brotli).Into(connection);

// Create the proxy using the invocation pipeline.
var hello = new HelloProxy(pipeline);

string greeting = await hello.SayHelloAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
