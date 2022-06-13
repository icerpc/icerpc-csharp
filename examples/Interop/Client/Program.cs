// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

await using var connection = new ClientConnection("ice://127.0.0.1:10000");

IHelloPrx hello = HelloPrx.FromConnection(connection, path: "/hello");

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    Console.WriteLine(await hello.SayHelloAsync(name));
}
