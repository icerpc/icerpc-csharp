// Copyright (c) ZeroC, Inc.

using IceRpc;
using OrderedServer;

await using var server = new Server(new ServerOptions
{
    ConnectionOptions = new ConnectionOptions
    {
        Dispatcher = new Scribe(),

        // Dispatch at most one request at a time per connection. This keeps the dispatches in order, since the ice
        // protocol delivers the requests received over a connection in order. Comment out this line to see the server
        // log the entries out of order.
        MaxDispatches = 1,
    },

    // Use the ice protocol and the default port for the ice protocol, 4061.
    ServerAddress = new ServerAddress(new Uri("ice://[::0]")),
});

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
