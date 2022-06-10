// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Retry;
using Microsoft.Extensions.Logging;

await using var connectionPool = new ConnectionPool(
    new ConnectionPoolOptions { PreferExistingConnection = true });

using var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddFilter("IceRpc", LogLevel.Debug);
        builder.AddConsole();
    });

var pipeline = new Pipeline()
    .UseLogger(loggerFactory)
    .UseRetry(
        new RetryOptions
        {
            MaxAttempts = 5
        },
        loggerFactory)
    .UseBinder(connectionProvider: connectionPool);

IHelloPrx hello = HelloPrx.Parse(
    "icerpc://127.0.0.1:10000/hello?alt-endpoint=127.0.0.1:10001&alt-endpoint=127.0.0.1:10002",
    invoker: pipeline);

Console.WriteLine($"hello: {hello.Proxy.Endpoint}");

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    Console.WriteLine(await hello.SayHelloAsync(name));
}
