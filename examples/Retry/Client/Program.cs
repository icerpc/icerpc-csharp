// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Retry;
using Microsoft.Extensions.Logging;

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

await using var connectionPool = new ConnectionPool(
    new ConnectionPoolOptions { PreferExistingConnection = true });

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddFilter("IceRpc", LogLevel.Information);
        builder.AddConsole();
    });

var pipeline = new Pipeline()
    .UseRetry(
        new RetryOptions
        {
            MaxAttempts = 5
        },
        loggerFactory)
    .UseBinder(connectionProvider: connectionPool)
    .UseLogger(loggerFactory);

IHelloPrx hello = HelloPrx.Parse("icerpc://127.0.0.1:10000/hello?alt-endpoint=127.0.0.1:10001", invoker: pipeline);

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    while (!cancellationSource.Token.IsCancellationRequested)
    {
        Console.WriteLine($"{await hello.SayHelloAsync(name)} Ctrl+C to exit");
        await Task.Delay(TimeSpan.FromSeconds(1));
    }
}
