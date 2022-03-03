using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    public ValueTask<string> SayHelloAsync(string name, Dispatch dispatch, CancellationToken cancel)
    {
        Console.WriteLine($"{name} says Hello!");
        return new($"Hello, {name}!");
    }
}
