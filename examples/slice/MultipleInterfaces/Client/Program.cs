// Copyright (c) ZeroC, Inc.

using IceRpc;
using Metrics;
using VisitorCenter;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

var greeter = new GreeterProxy(connection);
var requestCounter = new RequestCounterProxy(connection);

string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

int requestCount = await requestCounter.GetRequestCountAsync();

Console.WriteLine($"requests count: {requestCount}");

await connection.ShutdownAsync();
