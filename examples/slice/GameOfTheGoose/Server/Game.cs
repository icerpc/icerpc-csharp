// Copyright (c) ZeroC, Inc.

using GameOfTheGoose;
using ZeroC.Slice;

namespace GameOfTheGooseServer;

/// <summary>Pure game logic for the Game of the Goose. Not thread-safe — caller must synchronize.</summary>
internal class Game
{
    private GameState _state;
    private readonly int _numOfPlayers;
    private readonly List<int> _skipTurnPlayers = [];

    /// <summary>Creates a new game that requires <paramref name="numOfPlayers"/> players.</summary>
    internal Game(int numOfPlayers)
    {
        _state = new GameState
        {
            Players = [],
            Status = new GameStatus.WaitingForPlayers(),
            Turn = 0,
        };
        _numOfPlayers = numOfPlayers;
    }

    /// <summary>Gets the current status of the game.</summary>
    internal GameStatus Status => _state.Status;

    /// <summary>Adds a player to the game. Caller must check Status is WaitingForPlayers before calling.</summary>
    internal byte AddPlayer(string playerName, List<GameEvent> events)
    {
        byte playerIndex = (byte)_state.Players.Count;
        _state.Players.Add(new Player { Name = playerName, Position = 0 });
        events.Add(new GameEvent.PlayerJoined(new Player { Name = playerName }));

        if (_state.Players.Count == _numOfPlayers)
        {
            _state.Status = new GameStatus.Started();
            events.Add(new GameEvent.GameStarted());
        }

        return playerIndex;
    }

    /// <summary>Rolls dice for the given player and applies board rules.</summary>
    internal Result<byte, GameError> RollDice(byte playerIndex, List<GameEvent> events)
    {
        if (_state.Status is not GameStatus.Started)
        {
            return new GameError.GameNotInProgress();
        }

        if (playerIndex != _state.Turn)
        {
            return new GameError.NotPlayerTurn();
        }

        Player player = _state.Players[playerIndex];
        byte oldPosition = player.Position;

#pragma warning disable CA5394 // Random is fine for a board game dice roll — no security requirement
        byte diceRoll = (byte)Random.Shared.Next(1, 7);
#pragma warning restore CA5394
        events.Add(new GameEvent.DiceRolled(diceRoll));

        byte playerNewPosition = (byte)(oldPosition + diceRoll);
        int gooseIndex = Board.GooseSpaces.IndexOf(playerNewPosition);

        if (playerNewPosition >= Board.TrackSize - 1)
        {
            // Reached or overshot the end — player wins
            byte finalPosition = (byte)(Board.TrackSize - 1);
            events.Add(new GameEvent.PlayerMoved(playerIndex, oldPosition, finalPosition));
            player.Position = finalPosition;
            _state.Players[playerIndex] = player;
            _state.Status = new GameStatus.Finished();
            events.Add(new GameEvent.GameFinished(playerIndex));
        }
        else if (gooseIndex == Board.GooseSpaces.Count - 1)
        {
            // Landed on the last goose — bounce to the end and win
            byte finalPosition = Board.TrackSize - 1;
            events.Add(new GameEvent.PlayerMoved(playerIndex, oldPosition, playerNewPosition));
            events.Add(new GameEvent.GooseMoved(playerIndex, playerNewPosition, finalPosition));
            player.Position = finalPosition;
            _state.Players[playerIndex] = player;
            _state.Status = new GameStatus.Finished();
            events.Add(new GameEvent.GameFinished(playerIndex));
        }
        else if (gooseIndex != -1)
        {
            // Move the player to the goose space, then bounce to the next one
            byte goosePosition = playerNewPosition;
            playerNewPosition = Board.GooseSpaces[gooseIndex + 1];
            events.Add(new GameEvent.PlayerMoved(playerIndex, oldPosition, goosePosition));
            events.Add(new GameEvent.GooseMoved(playerIndex, goosePosition, playerNewPosition));
            player.Position = playerNewPosition;
            _state.Players[playerIndex] = player;
            // Landing on a goose space grants an extra turn
            events.Add(new GameEvent.ExtraTurn(playerIndex));
            events.Add(new GameEvent.TurnAdvanced(_state.Turn));
        }
        else
        {
            if (Board.SkipTurnSpaces.Contains(playerNewPosition))
            {
                _skipTurnPlayers.Add(playerIndex);
                events.Add(new GameEvent.Trapped(playerIndex));
            }
            events.Add(new GameEvent.PlayerMoved(playerIndex, oldPosition, playerNewPosition));
            player.Position = playerNewPosition;
            _state.Players[playerIndex] = player;
            for (int i = 0; i < _numOfPlayers; i++)
            {
                _state.Turn = (byte)((_state.Turn + 1) % _numOfPlayers);
                if (!_skipTurnPlayers.Contains(_state.Turn))
                {
                    break;
                }
                events.Add(new GameEvent.SkipTurn(_state.Turn));
                _skipTurnPlayers.Remove(_state.Turn);
            }
            events.Add(new GameEvent.TurnAdvanced(_state.Turn));
        }

        return diceRoll;
    }

    /// <summary>Returns a defensive copy of the current state.</summary>
    internal GameState GetSnapshot() => new([.. _state.Players], _state.Status, _state.Turn);
}
