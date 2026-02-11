// Copyright (c) ZeroC, Inc.

using GameOfTheGoose;

namespace GameOfTheGooseClient;

/// <summary>Thread-safe wrapper around GameState that synchronizes access from the main thread and the background
/// event listener.</summary>
internal class GameStateManager
{
    private GameState _state;
    private readonly Lock _lock = new();

    /// <summary>Creates a new manager initialized with the given <paramref name="state"/> snapshot.</summary>
    internal GameStateManager(GameState state) => _state = state;

    /// <summary>Gets the list of players. Accessed by FormatEvent on the same thread as ApplyEvent.</summary>
    internal IList<Player> Players => _state.Players;

    /// <summary>Gets the index of the player whose turn it is.</summary>
    internal byte Turn => _state.Turn;

    /// <summary>Applies a game event to the state. Called from the background event listener.</summary>
    internal void ApplyEvent(GameEvent gameEvent)
    {
        lock (_lock)
        {
            switch (gameEvent)
            {
                case GameEvent.PlayerJoined joined:
                    _state.Players.Add(joined.Player);
                    break;

                case GameEvent.PlayerMoved moved:
                    Player player = _state.Players[moved.Player];
                    player.Position = moved.NewPosition;
                    _state.Players[moved.Player] = player;
                    break;

                case GameEvent.GooseMoved gooseMoved:
                    Player goosePlayer = _state.Players[gooseMoved.Player];
                    goosePlayer.Position = gooseMoved.NewPosition;
                    _state.Players[gooseMoved.Player] = goosePlayer;
                    break;

                case GameEvent.TurnAdvanced turned:
                    _state.Turn = turned.NextPlayer;
                    break;

                case GameEvent.GameStarted:
                    _state.Status = new GameStatus.Started();
                    break;

                case GameEvent.GameFinished:
                    _state.Status = new GameStatus.Finished();
                    break;
            }
        }
    }

    /// <summary>Checks if the given player should roll. Called from the main thread.</summary>
    internal bool ShouldRoll(byte playerIndex)
    {
        lock (_lock)
        {
            return _state.Status is GameStatus.Started && _state.Turn == playerIndex;
        }
    }
}
