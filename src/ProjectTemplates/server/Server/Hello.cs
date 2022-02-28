using Demo;
using IceRpc;

public class Hello : Service, IHello
{
    public ValueTask<string> SayHelloAsync(string greeting, Dispatch dispatch, CancellationToken cancel)
    {
        Console.WriteLine("Hello World!");
        return new($"{greeting}, server!");
    }
}
