using Demo;
using IceRpc;

await using var server = new Server(new Hello());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
