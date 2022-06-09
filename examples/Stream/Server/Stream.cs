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

    public async ValueTask StreamNumbersAsync(
        IAsyncEnumerable<int> numbers,
        IFeatureCollection features,
        CancellationToken cancel)
    {
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancel, _cancellationToken);
        await foreach (var number in numbers.WithCancellation(cancellationSource.Token))
        {
            Console.WriteLine($"{number}");
        }
    }
}
