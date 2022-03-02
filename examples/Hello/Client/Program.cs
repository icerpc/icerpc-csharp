// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

await using var connection = new Connection("icerpc://127.0.0.1");

IHelloPrx hello = HelloPrx.FromConnection(connection);

Console.Write("Tell the server your name: ");

if (Console.ReadLine() is string name)
{
    await hello.SayHelloAsync(name);
}
