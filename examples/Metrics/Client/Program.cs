// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

// Establish the connection to the server
await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

IHelloProxy hello = new HelloProxy(connection);

double requestsPerSecond = 100;
Console.WriteLine($"Sending {requestsPerSecond} requests per second...");

// Cancel the client on Ctrl+C or Ctrl+Break
using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(1 / requestsPerSecond));

Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    periodicTimer.Dispose();
};

// Start invoking the remote method
try
{
    while (await periodicTimer.WaitForNextTickAsync())
    {
        await hello.SayHelloAsync();
    }
}
