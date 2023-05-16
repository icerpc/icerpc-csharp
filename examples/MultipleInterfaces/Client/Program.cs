// Copyright (c) ZeroC, Inc.

using IceRpc;
using Metrics;
using VisitorCenter;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

var greeterProxy = new GreeterProxy(connection);
var requestCounterProxy = new RequestCounterProxy(connection);

string greeting = await greeterProxy.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

int requestCount = await requestCounterProxy.GetRequestCountAsync();

Console.WriteLine($"requests count: {requestCount}");

await connection.ShutdownAsync();
