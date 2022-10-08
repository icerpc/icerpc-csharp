// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

var hello = new HelloProxy(connection);
string greeting = await hello.SayHelloAsync(Environment.UserName);

Console.WriteLine(greeting);
