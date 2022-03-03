using Demo;
using IceRpc;

await using var connection = new Connection("icerpc://127.0.0.1");

IHelloPrx hello = HelloPrx.FromConnection(connection);

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    Console.WriteLine(await hello.SayHelloAsync(name));
}
