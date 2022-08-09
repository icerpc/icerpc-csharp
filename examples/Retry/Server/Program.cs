// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using Microsoft.Extensions.Logging;

if (args.Length < 1)
{
    Console.WriteLine("Missing server number argument");
    return;
}

int number;
if (!int.TryParse(args[0], out number))
{
    Console.WriteLine($"Invalid server number argument '{args[0]}', expected a number");
    return;
}

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddFilter("IceRpc", LogLevel.Information);
    builder.AddSimpleConsole(configure => configure.IncludeScopes = true);
});

var serverAddress = new ServerAddress(new Uri($"icerpc://127.0.0.1:{10000 + number}/"));

await using var server = new Server(new Hello(serverAddress), serverAddress, loggerFactory: loggerFactory);

// Shuts down the server on Ctrl+C
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

server.Listen();
await server.ShutdownComplete;
