// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using System.Runtime.CompilerServices;

// Shuts down the client on Ctrl+C or Ctrl+Break
using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

// Establish the connection to the server
await using var connection = new ClientConnection("icerpc://127.0.0.1");
INumberStreamPrx numberStreamPrx = NumberStreamPrx.FromConnection(connection);

// Continues to stream data until either the client or server are shut down
Console.WriteLine("Client is streaming data...");
try
{
    await numberStreamPrx.StreamDataAsync(GetDataAsync(cancellationSource.Token));
}
catch (System.OperationCanceledException ex)
{
    Console.WriteLine($"Client streaming data was cancelled: {ex.Message}");
}

// Generates data to be streamed
static async IAsyncEnumerable<int> GetDataAsync([EnumeratorCancellation] CancellationToken cancel)
{
    int n = 0;
    // Continuously generating data to stream to the server
    while (!cancel.IsCancellationRequested)
    {
        yield return n++;
        await Task.Delay(TimeSpan.FromSeconds(1), cancel);
    }
}
