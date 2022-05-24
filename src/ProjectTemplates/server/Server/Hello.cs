using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    public ValueTask<string> SayHelloAsync(string name, IFeatureCollection features, CancellationToken cancel)
    {
        Console.WriteLine($"{name} says hello!");
        return new($"Hello, {name}!");
    }
}
