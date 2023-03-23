// Copyright (c) ZeroC, Inc.

using MultipleInterfacesExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

var helloProxy = new HelloProxy(connection);
var requestCounterProxy = new RequestCounterProxy(connection);

string greeting = await helloProxy.SayHelloAsync(Environment.UserName);

Console.WriteLine(greeting);

int requestCount = await requestCounterProxy.GetRequestCountAsync();

Console.WriteLine($"requests count: {requestCount}");

await connection.ShutdownAsync();
