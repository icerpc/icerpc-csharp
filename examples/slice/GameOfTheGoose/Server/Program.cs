// Copyright (c) ZeroC, Inc.

using GameOfTheGooseServer;
using IceRpc;
using System.CommandLine;
using System.Security.Cryptography.X509Certificates;

var playersOption = new Option<int>("--players", "-p")
{
    Description = "Number of players required to start the game (2-6)",
    DefaultValueFactory = _ => 2
};
playersOption.Validators.Add(result =>
{
    int value = result.GetValueOrDefault<int>();
    if (value is < 2 or > 6)
    {
        result.AddError("--players must be between 2 and 6.");
    }
});

var rootCommand = new RootCommand("Game of the Goose server")
{
    playersOption
};

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    int numOfPlayers = parseResult.GetValue(playersOption);

    // The default transport (QUIC) requires a server certificate. We use a test certificate here.
    using X509Certificate2 serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
        "../../../../certs/server.p12",
        password: null,
        keyStorageFlags: X509KeyStorageFlags.Exportable);

    var gameService = new GameService(numOfPlayers);

    // Create a server that dispatches all requests to the same service, an instance of GameService.
    Router router = new Router()
        .UseRequestContext()
        .UseDispatchInformation()
        .Mount("/", gameService);

    await using var server = new Server(
        router,
        serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate));

    server.Listen();

    Console.WriteLine($"Listening... (waiting for {numOfPlayers} players)");

    // Wait until the console receives a Ctrl+C.
    await CancelKeyPressed;
    await server.ShutdownAsync(cancellationToken);
});

await rootCommand.Parse(args).InvokeAsync();
