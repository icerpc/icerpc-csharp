// Copyright (c) ZeroC, Inc.

using IceRpc;
using VisitorCenter;

// Use the ice protocol for compatibility with ZeroC Ice. We use the default port for the ice protocol, 4061.
await using var connection = new ClientConnection(new Uri("ice://localhost"));

// The service address URI includes the protocol to use (ice).
var greeter = new GreeterProxy(connection, new Uri("ice:/greeter"));

string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
