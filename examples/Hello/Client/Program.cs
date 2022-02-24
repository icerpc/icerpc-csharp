// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

await using var connection = new Connection
{
    RemoteEndpoint = "icerpc://127.0.0.1:10000?tls=false"
};

IHelloPrx twoway = HelloPrx.FromConnection(connection);

Console.Write("Say Hello: ");

if (Console.ReadLine() is string greeting)
{
    Console.WriteLine(await twoway.SayHelloAsync(greeting));
}
