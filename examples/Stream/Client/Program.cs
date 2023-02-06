// Copyright (c) ZeroC, Inc.

using IceRpc;
using StreamExample;
using System.Runtime.CompilerServices;

// Establish the connection to the server
await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));
var numberStreamProxy = new NumberStreamProxy(connection);

// Continues to stream data until either the client or server are shut down
Console.WriteLine("Client is streaming data...");
await numberStreamProxy.StreamDataAsync(GetDataAsync(default));
Console.WriteLine("Client has finished streaming data.");

// Continuously generates data to be streamed

// The EnumeratorCancellation attribute allows user of the returned async enumerable to specify the cancellation token
// used by this method. The IceRPC runtime will provide a cancellation token that gets canceled when the stream used to
// transmit the async enumerable is canceled.
static async IAsyncEnumerable<int> GetDataAsync([EnumeratorCancellation] CancellationToken cancellationToken)
{
    // If the stream is canceled it is important to prevent GetDataAsync from leaking, we terminate the loop
    // once the cancellationToken is canceled.
    int n = 0;
    while (true)
    {
        yield return n++;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // TODO this is never executed, is this a bug?
            Console.WriteLine("The operation has been canceled by the server.");
            yield break;
        }
    }
}
