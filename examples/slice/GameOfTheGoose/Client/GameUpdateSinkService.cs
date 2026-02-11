// Copyright (c) ZeroC, Inc.

using GameOfTheGoose;
using IceRpc.Features;
using IceRpc.Slice;

namespace GameOfTheGooseClient;

/// <summary>Receives the game snapshot and event stream from the server and exposes them as properties.
/// Program.cs awaits <see cref="Received"/> to get the stream, then iterates it directly.</summary>
[SliceService]
internal partial class GameUpdateSinkService : IGameUpdateSinkService
{
    private readonly TaskCompletionSource<(GameSnapshot Snapshot, IAsyncEnumerable<GameUpdate> Events)> _received =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the server sends the initial snapshot and event stream.</summary>
    internal Task<(GameSnapshot Snapshot, IAsyncEnumerable<GameUpdate> Events)> Received => _received.Task;

    /// <inheritdoc/>
    public ValueTask OnGameUpdateAsync(
        GameSnapshot snapshot,
        IAsyncEnumerable<GameUpdate> @event,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        _received.SetResult((snapshot, @event));
        return default;
    }
}
