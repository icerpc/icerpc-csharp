// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Metrics;
using System.Diagnostics;

// Establish the connection to the server
await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

// Setup the invocation pipeline with the metrics interceptor
using var eventSource = InvocationEventSource.Log;
IInvoker pipeline = new Pipeline().UseMetrics(eventSource).Into(connection);

// Create the proxy using the invocation pipeline
var hello = new HelloProxy(pipeline);

Console.Write("Enter how many requests per second you want to send: ");

var input = Console.ReadLine();
double requestsPerSecond;

while (!double.TryParse(input, out requestsPerSecond))
{
    Console.Write("{0} is not a double. Please try again: ");
    input = Console.ReadLine();
}

Console.Write($"Sending {requestsPerSecond} requests per second...");

var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(1 / requestsPerSecond));
while (await periodicTimer.WaitForNextTickAsync())
{
    await hello.SayHelloAsync();
}
periodicTimer.Dispose();
