using IceRpc;
using IceRpc.Features;
using VisitorCenter;

namespace IceRpc_Ice_DI_Server;

/// <summary>A Chatbot is an IceRPC service that implements Ice interface 'Greeter' (defined in Greeter.ice).
/// </summary>
[Service]
internal partial class Chatbot : IGreeterService
{
    public ValueTask<string> GreetAsync(string name, IFeatureCollection features, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");
        return new($"Hello, {name}!");
    }
}
