// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc;
using Metrics;
using VisitorCenter;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

var greeter = new GreeterClient(connection);
var requestCounter = new RequestCounterClient(connection);

GreetResponse greetResponse = await greeter.GreetAsync(new GreetRequest { Name = Environment.UserName });

Console.WriteLine(greetResponse.Greeting);

GetRequestCountResponse requestCountResponse = await requestCounter.GetRequestCountAsync(new Empty());

Console.WriteLine($"requests count: {requestCountResponse.Count}");

await connection.ShutdownAsync();
