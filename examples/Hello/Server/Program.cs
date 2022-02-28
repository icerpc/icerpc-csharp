// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

try
{
    await using var server = new Server
    {
        Endpoint = "icerpc://127.0.0.1?tls=false",
        Dispatcher = new Hello()
    };

    // Destroy the server on Ctrl+C or Ctrl+Break
    Console.CancelKeyPress += (sender, eventArgs) =>
    {
        eventArgs.Cancel = true;
        _ = server.ShutdownAsync();
    };

    server.Listen();
    await server.ShutdownComplete;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

return 0;
