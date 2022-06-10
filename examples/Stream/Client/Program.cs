// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using System.Runtime.CompilerServices;

// Establish the connection to the server
await using var connection = new ClientConnection("icerpc://127.0.0.1");
INumberStreamPrx numberStreamPrx = NumberStreamPrx.FromConnection(connection);

// Continues to stream data until either the client or server are shut down
Console.WriteLine("Client is streaming data...");
try
{
    foreach (int index in Enumerable.Range(0, 3))
    {
        // A `default` cancellation token is passed into `GetDataAsync` since IceRpc will override the token via the
        // `[EnumeratorCancellation]` attribute
        await numberStreamPrx.StreamDataAsync(GetDataAsync(index * 10, default));
    }
}
catch (OperationCanceledException ex)
{
    Console.WriteLine($"Operation Canceled Exception: {ex.Message}");
}
Console.WriteLine("Client has finished streaming data.");

// Continuously generates data to be streamed

// This method has a `CancellationToken` parameter, which uses the `[EnumeratorCancellation]` attribute. IceRpc will
// automatically override the `CancellationToken` via this attribute when the server cancels the stream.
static async IAsyncEnumerable<int> GetDataAsync(int n, [EnumeratorCancellation] CancellationToken cancel)
{
    // If the service or server cancels the stream it is important to prevent GetDataAsync from leaking. When the
    // cancellation token is canceled, `Task.Delay(TimeSpan.FromSeconds(1), cancel);` will throw an
    // OperationCanceledException that can be used to break from the while loop.
    while (true)
    {
        yield return n++;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancel);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("The operation has been canceled by the server.");
            yield break;
        }
    }
}
