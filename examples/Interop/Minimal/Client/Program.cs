// Copyright (c) ZeroC, Inc.

using Demo;
using IceRpc;

// Use the ice protocol for compatibility with ZeroC Ice.
await using var connection = new ClientConnection(new Uri("ice://localhost:10000"));

// The service address URI includes the protocol to use (ice).
var helloProxy = new HelloProxy(connection, new Uri("ice:/hello"));

Console.WriteLine("Saying hello to the server...");

await helloProxy.SayHelloAsync();

await connection.ShutdownAsync();
