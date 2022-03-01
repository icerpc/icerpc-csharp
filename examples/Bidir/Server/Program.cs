using Demo;
using IceRpc;

try
{
    AlertSystem alertSystem = new AlertSystem();
    await using var server = new Server
    {
        Endpoint = "icerpc://127.0.0.1:10000?tls=false",
        Dispatcher = alertSystem
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
