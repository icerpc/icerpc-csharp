// Copyright (c) ZeroC, Inc.

using GameOfTheGoose;

using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace GameOfTheGooseServer;

/// <summary>
/// The source of game updates for a single connected player. Wraps an unbounded channel whose writer side is fed
/// synchronously by BroadcastEvents (under lock), and whose reader side is consumed as an IAsyncEnumerable by the IceRPC
/// streaming call to OnGameUpdateAsync.
/// </summary>
internal sealed class GameUpdateSource
{
    private readonly Channel<GameUpdate> _channel = Channel.CreateUnbounded<GameUpdate>();

    /// <summary>Synchronously writes a game update into the channel. Safe to call under Lock because TryWrite on an
    /// unbounded channel never blocks.</summary>
    public bool TryWrite(GameUpdate update) => _channel.Writer.TryWrite(update);

    /// <summary>Marks the channel as complete, signaling the reader (and thus the IceRPC stream) that no more updates
    /// will be sent.</summary>
    public void Complete() => _channel.Writer.Complete();

    /// <summary>Returns the channel contents as an IAsyncEnumerable suitable for passing to
    /// GameUpdateSinkProxy.OnGameUpdateAsync as the stream parameter.</summary>
    public async IAsyncEnumerable<GameUpdate> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (GameUpdate update in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return update;
        }
    }
}
