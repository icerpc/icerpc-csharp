// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Metrics;
using System.Diagnostics;

// Establish the connection to the server
await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

// Setup the invocation pipeline with the metrics interceptor
IInvoker pipeline = new Pipeline().UseMetrics(InvocationEventSource.Log).Into(connection);

// Create the proxy using the invocation pipeline
var proxy = new MetricsProxy(pipeline);

// Gather necessary user input
Console.Write("Enter how many requests per second you want to send: ");

double requestsPerSecond;
while (!double.TryParse(Console.ReadLine(), out requestsPerSecond))
{
    Console.Write($"{requestsPerSecond} is not a double. Please try again: ");
}

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
        await proxy.PingAsync();
    }
}
catch (OperationCanceledException)
{
}
