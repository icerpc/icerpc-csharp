# IceRPC

IceRPC is a modular RPC framework that helps you build networked applications with minimal effort. The IceRpc assembly
and package represent the base assembly and package for the [C# implementation of IceRPC][icerpc-csharp].

[Package][package] | [Source code][source] | [Getting started][getting-started] | [Examples][examples] | [Documentation][docs] | [API reference][api]

## Sample Code

```slice
// Slice contract

module VisitorCenter

/// Represents a simple greeter.
interface Greeter {
    /// Creates a personalized greeting.
    /// @param name: The name of the person to greet.
    /// @returns: The greeting.
    greet(name: string) -> string
}
```

```csharp
// Client application

using IceRpc;
using VisitorCenter;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

var greeterProxy = new GreeterProxy(connection);
string greeting = await greeterProxy.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
```

```csharp
// Server application

using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;
using VisitorCenter;

await using var server = new Server(new Chatbot());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();

internal class Chatbot : Service, IGreeterService
{
    public ValueTask<string> GreetAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");
        return new($"Hello, {name}!");
    }
}
```

[api]: https://api.icerpc.com/csharp/api/
[docs]:https://docs.testing.zeroc.com
[getting-started]: https://docs.testing.zeroc.com/getting-started
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[examples]: https://github.com/icerpc/icerpc-csharp/tree/main/examples
[package]: https://www.nuget.org/packages/IceRpc
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc
