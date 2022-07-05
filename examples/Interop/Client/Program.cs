// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

// Use the ice protocol for compatibility with ZeroC Ice.
await using var connection = new ClientConnection("ice://127.0.0.1:10000");

// Don't forget to specify the protocol when constructing your proxy. The default protocol is icerpc.
var hello = new HelloProxy(connection, "/hello", connection.Protocol);

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    Console.WriteLine(await hello.SayHelloAsync(name));
}
