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
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        uint count = 0;
        await foreach (int number in numbers.WithCancellation(cts.Token))
        {
            // After receiving 10 numbers, cancel the token which will propagate the cancellation to the client
            // async enumerable.
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
