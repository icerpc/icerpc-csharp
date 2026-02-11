// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace GameOfTheGooseServer;

/// <summary>Represents the game board for our simplified version of the Game of the Goose. The board has 40 spaces,
/// and players start at space 0. The first player to reach space 39 wins.</summary>
internal static class Board
{
    /// <summary>The number of spaces on the board. Players start at space 0 and the first to reach space 39
    /// wins.</summary>
    public const byte TrackSize = 40;

    /// <summary>Goose spaces that move you to the next goose space and grant you an extra turn. If you land on
    /// space 5, you move to space 9 and get an extra turn. Landing on the last goose space moves you to the final
    /// track space.</summary>
    public static readonly ImmutableList<byte> GooseSpaces = [5, 9, 14, 18, 23, 27, 32];

    /// <summary>Bad luck spaces that cause you to skip your next turn.</summary>
    public static readonly ImmutableList<byte> SkipTurnSpaces = [6, 12];
}
