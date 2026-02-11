// Copyright (c) ZeroC, Inc.

using GameOfTheGoose;
using GameOfTheGooseClient;
using IceRpc;
using IceRpc.Features;
using System.Security.Cryptography.X509Certificates;
using ZeroC.Slice;

// Load the test root CA certificate.
using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

string? playerName;
do
{
    Console.Write("Enter your name: ");
    playerName = Console.ReadLine()?.Trim();
}
while (string.IsNullOrEmpty(playerName));

var sink = new GameUpdateSinkService();

// Create a secure client connection with a dispatcher so the server can call us back.
await using var connection = new ClientConnection(
    new ClientConnectionOptions
    {
        Dispatcher = sink,
        ServerAddress = new ServerAddress(new Uri("icerpc://localhost")),
        ClientAuthenticationOptions = CreateClientAuthenticationOptions(rootCA)
    });

// Set up the invocation pipeline with request context support.
Pipeline pipeline = new Pipeline().UseRequestContext().Into(connection);

var gameRoom = new GameRoomProxy(pipeline);
var gameSession = new GameSessionProxy(pipeline);

// Join the game.
Console.WriteLine($"Joining as '{playerName}'...");
JoinResult joinResult = await gameRoom.JoinAsync(playerName);

if (joinResult is not JoinResult.Success joined)
{
    Console.WriteLine($"Failed to join: {joinResult}");
    return;
}
string sessionId = joined.SessionId;
byte myPlayerIndex = joined.PlayerIndex;

// Receive the snapshot and event stream from the server.
var (snapshot, events) = await sink.Received;
var state = new GameStateManager(snapshot.State);

if (snapshot.State.Status is GameStatus.Started)
{
    Console.WriteLine(state.Turn == myPlayerIndex
        ? $"Joined! The game has started. {Ansi.Bold}{Ansi.Yellow}It's your turn{Ansi.Reset}"
        : $"Joined! The game has started. It's {state.Players[state.Turn].Name}'s turn");
}
else
{
    Console.WriteLine($"Joined! Waiting for more players ({state.Players.Count} so far)...");
}

// Build the features with our session ID for all RollDice calls.
IFeatureCollection sessionFeatures = new FeatureCollection().With<IRequestContextFeature>(
    new RequestContextFeature { ["sessionId"] = sessionId });

// Listen for game events in the background. Canceled on quit or Ctrl+C.
using var cts = new CancellationTokenSource();
_ = CancelKeyPressed.ContinueWith(_ => cts.Cancel(), TaskScheduler.Default);
ulong expectedSeq = snapshot.LastSeq + 1;
_ = Task.Run(async () =>
{
    await foreach (GameUpdate update in events.WithCancellation(cts.Token))
    {
        if (update.Seq != expectedSeq)
        {
            Console.Error.WriteLine(
                $"Out-of-order update: expected seq {expectedSeq}, got {update.Seq}. Disconnecting.");
            cts.Cancel();
            break;
        }
        expectedSeq++;

        string message = FormatEvent(update.GameEvent);
        state.ApplyEvent(update.GameEvent);
        if (message.Length > 0)
        {
            Console.WriteLine($"  >> {message}");
        }
    }
});

// Game loop: press Enter to roll when it's your turn, or type "q" to quit.
Console.WriteLine("Press Enter to roll when it's your turn. Type 'q' to quit.\n");

while (!cts.IsCancellationRequested)
{
    string? input = Console.ReadLine()?.Trim();
    if (input is null or "q" or "quit")
    {
        cts.Cancel();
        break;
    }

    if (input.Length == 0)
    {
        if (state.ShouldRoll(myPlayerIndex))
        {
            var result = await gameSession.RollDiceAsync(sessionFeatures);
            if (result is Result<byte, GameError>.Failure failure)
            {
                Console.WriteLine($"  Error: {failure.Value}");
            }
        }
    }
}

await connection.ShutdownAsync();

// Format an event into a display message. Called before ApplyEvent on the same thread.
string FormatEvent(GameEvent gameEvent) => gameEvent switch
{
    GameEvent.PlayerJoined joined =>
        $"{joined.Player.Name} joined the game",

    GameEvent.DiceRolled rolled =>
        $"{state.Players[state.Turn].Name} rolled a {rolled.Roll}",

    GameEvent.PlayerMoved moved =>
        $"{state.Players[moved.Player].Name} moved: {moved.OldPosition} -> {moved.NewPosition}",

    GameEvent.GooseMoved moved =>
        $"{state.Players[moved.Player].Name} bounced: {moved.OldPosition} -> {moved.NewPosition} (goose)",

    GameEvent.ExtraTurn extra =>
        $"{state.Players[extra.Player].Name} gets an extra turn (goose)",

    GameEvent.SkipTurn skip =>
        $"{state.Players[skip.Player].Name}'s turn will be skipped (trap)",

    GameEvent.TurnAdvanced turned =>
        turned.NextPlayer == myPlayerIndex
            ? $"{Ansi.Bold}{Ansi.Yellow}It's your turn{Ansi.Reset}"
            : $"It's {state.Players[turned.NextPlayer].Name}'s turn",

    GameEvent.GameStarted =>
        state.Turn == myPlayerIndex
            ? $"The game has started! {Ansi.Bold}{Ansi.Yellow}It's your turn{Ansi.Reset}"
            : $"The game has started! It's {state.Players[state.Turn].Name}'s turn",

    GameEvent.GameFinished finished =>
        $"{state.Players[finished.Player].Name} wins!",

    _ => ""
};

file static class Ansi
{
    public const string Reset = "\x1b[0m";
    public const string Bold = "\x1b[1m";
    public const string Yellow = "\x1b[33m";
}
