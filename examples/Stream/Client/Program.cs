using Demo;
using IceRpc;
using System.Runtime.CompilerServices;

var options = new ClientConnectionOptions
{
    RemoteEndpoint = "icerpc://127.0.0.1",
};

await using var connection = new ClientConnection(options);
INumberStreamPrx numberStreamPrx = NumberStreamPrx.FromConnection(connection);

// Shuts down the client on Ctrl+C or Ctrl+Break
using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

static async IAsyncEnumerable<int> GetDataAsync([EnumeratorCancellation] CancellationToken cancel)
{
    await Task.Yield();

    // Generate the nth fibonacci number
    int n = 0;
    static int NthFibonacciNumber(int n)
    {
        if ((n == 0) || (n == 1)) return n;
        else
        {
            return (NthFibonacciNumber(n - 1) + NthFibonacciNumber(n - 2));
        }
    }

    // Continuously generating data to stream to the server
    while (!cancel.IsCancellationRequested)
    {
        yield return NthFibonacciNumber(n);
        await Task.Delay(TimeSpan.FromSeconds(1), cancel);
        n++;
    }
}

Console.WriteLine("Client is streaming data...");

await numberStreamPrx.StreamNumbersAsync(GetDataAsync(cancellationSource.Token));
