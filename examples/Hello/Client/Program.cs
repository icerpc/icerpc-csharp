// Copyright (c) ZeroC, Inc.

using HelloExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

var helloProxy = new HelloProxy(connection);
string greeting = await helloProxy.SayHelloAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
