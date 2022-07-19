using Demo;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

IHelloProxy hello = new HelloProxy(connection);

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    Console.WriteLine(await hello.SayHelloAsync(name));
}
