using GreetExample;
using IceRpc;

await using var server = new Server(new Chatbot());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
