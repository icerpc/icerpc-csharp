// Copyright (c) ZeroC, Inc.

using CompressExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Add the compressor interceptor to the invocation pipeline.
IInvoker pipeline = new Pipeline().UseCompressor(CompressionFormat.Brotli).Into(connection);

// Create the proxy using the invocation pipeline.
var greeter = new GreeterProxy(pipeline);

string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
