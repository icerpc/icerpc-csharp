// Copyright (c) ZeroC, Inc.

using GreeterServer;
using IceRpc;

// Create 3 servers, each with its own service.
var serverList = new List<Server>
{
    new Server(new EnglishChatbot(), new Uri("icerpc://[::0]:10000")),
    new Server(new FrenchChatbot(), new Uri("icerpc://[::0]:10001")),
    new Server(new SpanishChatbot(), new Uri("icerpc://[::0]:10002"))
};

try
{
    foreach (var server in serverList)
    {
        server.Listen();
    }

    // Wait until the console receives a Ctrl+C.
    await CancelKeyPressed;

    await Task.WhenAll(serverList.Select(server => server.ShutdownAsync()));
}
finally
{
    await Task.WhenAll(serverList.Select(server => server.DisposeAsync().AsTask()));
}
