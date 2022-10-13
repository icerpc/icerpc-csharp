// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace Demo;

public class NumberStream : Service, INumberStream
{

    public async ValueTask StreamDataAsync(
        IAsyncEnumerable<int> numbers,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        // Combine the IceRpc cancellation token with the local cancellation token used for handling Ctrl+C or
        // Ctrl+Break events. This is used to notify the client that the server is shutting down.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        uint count = 0;
        await foreach (int number in numbers.WithCancellation(cts.Token))
        {
            // After receiving 10 numbers, cancel the stream
            if (count == 10)
            {
                cts.Cancel();
                break;
            }
            else
            {
                Console.WriteLine($"{number}");
            }
            count++;
        }
    }
}
