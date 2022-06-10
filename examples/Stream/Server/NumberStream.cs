// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace Demo;

public class NumberStream : Service, INumberStream
{
    CancellationToken _cancellationToken;

    public NumberStream(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    public async ValueTask StreamDataAsync(
        IAsyncEnumerable<int> numbers,
        IFeatureCollection features,
        CancellationToken cancel)
    {
        // Combine the IceRpc cancellation token with the local cancellation token used for handling Ctrl+C or
        // Ctrl+Break events. This is used to notify the client that the server is shutting down.
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancel, _cancellationToken);
        uint count = 0;
        await foreach (var number in numbers.WithCancellation(cancellationSource.Token))
        {
            // After receiving 10 numbers, cancel the stream
            if (count == 10)
            {
                cancellationSource.Cancel();
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
