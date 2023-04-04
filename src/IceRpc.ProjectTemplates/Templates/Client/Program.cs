using GreeterExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

var greeterProxy = new GreeterProxy(connection);
string greeting = await greeterProxy.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);
