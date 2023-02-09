// Copyright (c) ZeroC, Inc.

using MetricsExample;
using IceRpc;

// Establish the connection to the server
await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

var hello = new HelloProxy(connection);

double requestsPerSecond = 20;
Console.WriteLine($"Sending {requestsPerSecond} requests per second...");

using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(1 / requestsPerSecond));

// Stop the client on Ctrl+C
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    periodicTimer.Dispose();
};

// Start invoking the remote method
while (await periodicTimer.WaitForNextTickAsync())
{
    await hello.SayHelloAsync();
}
