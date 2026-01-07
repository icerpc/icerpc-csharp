// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using VisitorCenter;

// Create a connection that uses for the TCP client transport.
// Since TcpClientTransport is a duplex transport, we wrap it in a SlicClientTransport to get a multiplexed transport.
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    multiplexedClientTransport: new SlicClientTransport(new TcpClientTransport()));

var greeter = new GreeterProxy(connection);
string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
