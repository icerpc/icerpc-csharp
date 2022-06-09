// Copyright (c) ZeroC, Inc. All rights reserved.

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
    // Generate the nth fibonacci number
    int n = 0;
    // Continuously generating data to stream to the server
    while (!cancel.IsCancellationRequested)
    {
        yield return n;
        await Task.Delay(TimeSpan.FromSeconds(1), cancel);
        n++;
    }
}

Console.WriteLine("Client is streaming data...");

await numberStreamPrx.StreamNumbersAsync(GetDataAsync(cancellationSource.Token));
