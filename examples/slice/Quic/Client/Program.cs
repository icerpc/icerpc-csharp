// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Quic;
using VisitorCenter;

// Create a connection that uses the test Root CA, and the QUIC multiplexed transport.
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(),
    multiplexedClientTransport: new QuicClientTransport());

var greeter = new GreeterProxy(connection);
string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
