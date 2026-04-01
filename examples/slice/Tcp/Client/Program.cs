// Copyright (c) ZeroC, Inc.

using IceRpc;
using VisitorCenter;

// Create a connection that uses the TCP client transport instead of the default, QUIC.
await using var connection = new ClientConnection(new Uri("icerpc://localhost?transport=tcp"));

var greeter = new GreeterProxy(connection);
string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
