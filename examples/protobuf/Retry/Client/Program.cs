// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Retry;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using VisitorCenter;

if (args.Length < 1)
{
    Console.WriteLine("Missing server instances argument");
    return;
}

int serverInstances;
if (!int.TryParse(args[0], out serverInstances))
{
    Console.WriteLine($"Invalid server instances argument '{args[0]}', expected a number");
    return;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddFilter("IceRpc", LogLevel.Information);
        builder.AddSimpleConsole(configure => configure.IncludeScopes = true);
    });

await using var connectionCache = new ConnectionCache();

// Create an invocation pipeline with the retry and logger interceptors.
Pipeline pipeline = new Pipeline()
    .UseRetry(
        new RetryOptions
        {
            // Make up to 5 attempts before giving up.
            MaxAttempts = 5
        },
        loggerFactory)
    .UseLogger(loggerFactory)
    .Into(connectionCache);

// We use a logger to ensure proper ordering of the messages on the console.
ILogger logger = loggerFactory.CreateLogger("IceRpc.RetryExample");

// Create a service address containing a server address for each of the servers. The address of the main server is
// set as the main server address, and the remaining server addresses are added to the alt server addresses.
var greeterServiceAddress = new ServiceAddress(new Uri("icerpc://localhost:10000/greeter"))
{
    AltServerAddresses = Enumerable.Range(1, serverInstances - 1)
        .Select(i => new ServerAddress { Host = "localhost", Port = (ushort)(i + 10000) })
        .ToImmutableList()
};

var greeter = new GreeterClient(pipeline, greeterServiceAddress);

CancellationToken cancellationToken = cts.Token;
try
{
    while (true)
    {
        GreetResponse greetResponse = await greeter.GreetAsync(
            new GreetRequest { Name = Environment.UserName },
            cancellationToken: cancellationToken);
        logger.LogResponse(greetResponse.Greeting);
        logger.LogLooping();
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
    }
}
catch (DispatchException dispatchException)
{
    // The request failed because we reached the allowed max attempts or because all server addresses were excluded
    // due to the failure retry policy.
    logger.LogException(dispatchException);
}
catch (OperationCanceledException)
{
    // Expected, from Ctrl+C.
}

await connectionCache.ShutdownAsync();

internal static partial class RetryExampleLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "Response",
        Level = LogLevel.Information,
        Message = "Server says {Message}")]
    internal static partial void LogResponse(this ILogger logger, string message);

    [LoggerMessage(
        EventId = 2,
        EventName = "Looping",
        Level = LogLevel.Information,
        Message = "Looping in 3 seconds, press Ctrl+C to exit\n")]
    internal static partial void LogLooping(this ILogger logger);

    [LoggerMessage(
        EventId = 3,
        EventName = "Exception",
        Level = LogLevel.Error,
        Message = "Invocation failed with an exception, exiting")]
    internal static partial void LogException(this ILogger logger, Exception exception);
}
