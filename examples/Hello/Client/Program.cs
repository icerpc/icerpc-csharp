// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

await using var connection = new Connection("icerpc://127.0.0.1");

IHelloPrx hello = HelloPrx.FromConnection(connection);

Console.Write("Say Hello: ");

if (Console.ReadLine() is string greeting)
{
    Console.WriteLine(await hello.SayHelloAsync(greeting));
}
