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

    public async ValueTask StreamNumbersAsync(IAsyncEnumerable<int> numbers, IFeatureCollection features, CancellationToken cancel)
    {
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancel, _cancellationToken);
        cancel = cancellationSource.Token;

        IAsyncEnumerator<int> enumerator = numbers.GetAsyncEnumerator(cancel);
        try
        {
            while (await enumerator.MoveNextAsync() && !cancel.IsCancellationRequested)
            {
                Console.WriteLine($"{enumerator.Current}");
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }
}
