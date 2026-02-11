// Copyright (c) ZeroC, Inc.

using GameOfTheGoose;
using IceRpc.Features;
using IceRpc.Slice;
using ZeroC.Slice;

namespace GameOfTheGooseServer;

/// <summary>IceRPC service that implements both GameRoom (join) and GameSession (roll dice). Delegates all game
/// logic to <see cref="Game"/> and manages sessions, event broadcasting, and client streaming.</summary>
[SliceService]
internal partial class GameService : IGameRoomService, IGameSessionService
{
    /// <summary>The game logic instance. Protected by <see cref="_lock"/>.</summary>
    private readonly Game _game;

    /// <summary>Synchronizes access to <see cref="_game"/>, <see cref="_sessions"/>, <see cref="_sources"/>, and
    /// <see cref="_seq"/>.</summary>
    private readonly Lock _lock = new();

    /// <summary>Maps session IDs (issued at join time) to player indices.</summary>
    private readonly Dictionary<string, byte> _sessions = [];

    /// <summary>Monotonically increasing sequence number assigned to each broadcast event.</summary>
    private ulong _seq;

    /// <summary>Per-client update channels; each entry feeds one streaming call to the client's sink.</summary>
    private readonly List<GameUpdateSource> _sources = [];

    /// <summary>Creates a new game service for a game that requires <paramref name="numOfPlayers"/> players.</summary>
    public GameService(int numOfPlayers) => _game = new Game(numOfPlayers);

    /// <inheritdoc/>
    public ValueTask<Result<byte, GameError>> RollDiceAsync(
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        IRequestContextFeature? contextFeature = features.Get<IRequestContextFeature>();
        if (contextFeature is null)
        {
            Result<byte, GameError> result = new GameError.InvalidSessionId();
            return new ValueTask<Result<byte, GameError>>(result);
        }

        if (!contextFeature.Value.TryGetValue("sessionId", out string? sessionId))
        {
            Result<byte, GameError> result = new GameError.InvalidSessionId();
            return new ValueTask<Result<byte, GameError>>(result);
        }

        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out byte playerIndex))
            {
                Result<byte, GameError> result = new GameError.InvalidSessionId();
                return new ValueTask<Result<byte, GameError>>(result);
            }

            var events = new List<GameEvent>();
            Result<byte, GameError> rollResult = _game.RollDice(playerIndex, events);
            if (rollResult is Result<byte, GameError>.Success)
            {
                BroadcastEvents(events);
                if (_game.Status is GameStatus.Finished)
                {
                    // Complete all update sources so streaming calls end gracefully
                    foreach (GameUpdateSource s in _sources)
                    {
                        s.Complete();
                    }
                }
            }

            return new ValueTask<Result<byte, GameError>>(rollResult);
        }
    }

    /// <inheritdoc/>
    public ValueTask<JoinResult> JoinAsync(
        string playerName,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        // Create a callback proxy from the connection's invoker.
        IDispatchInformationFeature? dispatchInfo = features.Get<IDispatchInformationFeature>()
            ?? throw new InvalidOperationException(
                "IDispatchInformationFeature is not available. Ensure UseDispatchInformation() is added to the router.");
        var gameUpdateSink = new GameUpdateSinkProxy(dispatchInfo.ConnectionContext.Invoker);

        GameUpdateSource? source = null;
        GameSnapshot snapshot = default;
        JoinResult result;

        lock (_lock)
        {
            result = _game.Status switch
            {
                GameStatus.Started => new JoinResult.Failure(new GameError.GameAlreadyStarted()),
                GameStatus.Finished => new JoinResult.Failure(new GameError.GameAlreadyFinished()),
                _ => Join(playerName)
            };
        }

        // Outside lock: start streaming updates to the client's sink
        if (source is not null)
        {
            _ = StreamUpdatesAsync(gameUpdateSink, snapshot, source);
        }

        return new ValueTask<JoinResult>(result);

        JoinResult.Success Join(string playerName)
        {
            string sessionId = Guid.NewGuid().ToString();

            var events = new List<GameEvent>();
            byte playerIndex = _game.AddPlayer(playerName, events);
            _sessions[sessionId] = playerIndex;

            // Broadcast to existing sources before creating the new one
            BroadcastEvents(events);

            // Snapshot current state for the new player
            snapshot = new GameSnapshot(_game.GetSnapshot(), _seq);

            // Create source and register for future event broadcasts
            source = new GameUpdateSource();
            _sources.Add(source);

            return new JoinResult.Success(sessionId, playerIndex);
        }
    }

    /// <summary>Calls OnGameUpdateAsync on the client's sink proxy, streaming updates from the source. Runs as a
    /// fire-and-forget task. If the client disconnects or the call fails, the source's channel is completed for
    /// cleanup.</summary>
    private static async Task StreamUpdatesAsync(
        GameUpdateSinkProxy sink,
        GameSnapshot snapshot,
        GameUpdateSource source)
    {
        try
        {
            await sink.OnGameUpdateAsync(snapshot, source.ReadAllAsync());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StreamUpdates] exception: {ex}");
            // Client disconnected or network error — complete channel for cleanup.
            // Source stays in _sources; future TryWrite returns false (harmless).
            source.Complete();
        }
    }

    /// <summary>Writes each event as a sequenced <see cref="GameUpdate"/> to all connected client sources. Must be
    /// called under <see cref="_lock"/>.</summary>
    private void BroadcastEvents(List<GameEvent> events)
    {
        foreach (GameEvent gameEvent in events)
        {
            _seq++;
            var update = new GameUpdate(_seq, gameEvent);
            foreach (GameUpdateSource source in _sources)
            {
                source.TryWrite(update);
            }
        }
    }
}
