using Demo;
using IceRpc;

await using var connection = new Connection
{
    RemoteEndpoint = "icerpc://127.0.0.1?tls=false"
};

IHelloPrx hello = HelloPrx.FromConnection(connection);

Console.Write("Say Hello: ");

if (Console.ReadLine() is string greeting)
{
    Console.WriteLine(await hello.SayHelloAsync(greeting));
}
