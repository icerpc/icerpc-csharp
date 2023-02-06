// Copyright (c) ZeroC, Inc.

using HelloExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

var helloProxy = new HelloProxy(connection);
string greeting = await helloProxy.SayHelloAsync(Environment.UserName);

Console.WriteLine(greeting);
