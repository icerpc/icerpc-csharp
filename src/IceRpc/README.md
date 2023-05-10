# IceRpc

IceRpc provides the C# implementation for [IceRPC][icerpc].

[Source code][source] | [Package][package] | [Example][example] | [API reference documentation][api]

## Sample Code

```csharp
// Client application

using GreeterExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

var greeterProxy = new GreeterProxy(connection);
string greeting = await greeterProxy.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
```

```csharp
// Server application
using GreeterExample;
using IceRpc;

await using var server = new Server(...);
server.Listen();
```

[api]: https://api.icerpc.com/csharp/api/
[icerpc]:https://docs.testing.zeroc.com/docs/
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/Greeter
[package]: https://www.nuget.org/packages/IceRpc
[source]: https://github.com/icerpc/icerpc-csharp
