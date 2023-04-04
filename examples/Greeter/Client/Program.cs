// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

var greeterProxy = new GreeterProxy(connection);
string greeting = await greeterProxy.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
