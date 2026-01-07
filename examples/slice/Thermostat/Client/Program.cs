// Copyright (c) ZeroC, Inc.

using IceRpc;
using Igloo;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Security.Cryptography.X509Certificates;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Information));

// Load the test root CA certificate in order to connect to the server that uses a test server certificate.
using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

// Create a secure client connection to Server using the default transport (QUIC).
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA),
    logger: loggerFactory.CreateLogger<ClientConnection>());

Pipeline pipeline = new Pipeline()
    .UseDeadline(TimeSpan.FromSeconds(3))
    .UseLogger(loggerFactory)
    .Into(connection);

var thermostat = new ThermostatProxy(pipeline);

// This client provides two commands: monitor and set.
// monitor is the default and streams readings until your press Ctrl+C.
// set <value> changes the set point and exits.

var monitorCommand = new Command("monitor", "Monitor the thermostat (default)");
monitorCommand.SetAction(MonitorAsync);

var setCommand = new Command("set", "Change the set point");
var setArgument = new Argument<float>("setPoint");
setCommand.Arguments.Add(setArgument);
setCommand.SetAction(ChangeSetPointAsync);

var rootCommand = new RootCommand()
{
    monitorCommand,
    setCommand
};
rootCommand.Action = monitorCommand.Action;

await rootCommand.Parse(args).InvokeAsync();

// Graceful shutdown
await connection.ShutdownAsync();

async Task ChangeSetPointAsync(ParseResult parseResult, CancellationToken cancellationToken)
{
    float value = parseResult.GetRequiredValue(setArgument);
    try
    {
        await thermostat.ChangeSetPointAsync(value, cancellationToken: cancellationToken);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Successfully changed set point to {value}°F.");
    }
    catch (DispatchException exception)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(
            $"Failed to change set point to {value}°F: StatusCode = {exception.StatusCode}, Message = {exception.Message}");
    }
    catch (Exception exception)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed to change set point to {value}°F: {exception}");
    }
    Console.ResetColor();
}

async Task MonitorAsync(ParseResult parseResult, CancellationToken cancellationToken)
{
    IAsyncEnumerable<Reading> readings = await thermostat.MonitorAsync(cancellationToken: cancellationToken);

    // The iteration completes when cancellationToken is canceled.
    await foreach (Reading reading in readings.WithCancellation(cancellationToken))
    {
        Console.WriteLine(reading);
    }
}
