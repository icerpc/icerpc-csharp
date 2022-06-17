using Demo;
using IceRpc;

Console.WriteLine("Press type a password that the client will need use to authenticate:");
if (Console.ReadLine() is string password)
{
    // Adding authentication middleware to the router
    Router router = new Router().Use(next => new AuthenticationMiddleware(next, password));
    router.Map<IHello>(new Hello());

    await using var server = new Server(router);

    // Shuts down the server on Ctrl+C or Ctrl+Break
    Console.CancelKeyPress += (sender, eventArgs) =>
    {
        eventArgs.Cancel = true;
        _ = server.ShutdownAsync();
    };

    // Start the server
    server.Listen();

    Console.WriteLine("Server is waiting for connections...");

    await server.ShutdownComplete;
}
