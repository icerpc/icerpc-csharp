// Copyright (c) ZeroC, Inc.

using Demo;
using IceRpc;
using IceRpc.Retry;
using IceRpc.Slice.Ice;
using Microsoft.Extensions.Logging;

// Create an invocation pipeline for all our proxies.
var pipeline = new Pipeline();

// Create a locator proxy with the invocation pipeline as its invoker.
var locator = new LocatorProxy(pipeline, new Uri("ice://localhost/DemoIceGrid/Locator"));

// Create a logger factory that logs to the console.
using ILoggerFactory loggerFactory = LoggerFactory.Create(
    builder =>
    {
        builder.AddFilter("IceRpc", LogLevel.Information);
        builder.AddSimpleConsole(configure => configure.IncludeScopes = true);
    });

// Create a connection cache. A locator interceptor is typically installed in an invocation pipeline that flows into
// a connection cache.
await using var connectionCache = new ConnectionCache();

// Add the retry, locator, and logger interceptors to the pipeline.
pipeline = pipeline
    .UseRetry(new RetryOptions(), loggerFactory)
    .UseLocator(locator, loggerFactory)
    .UseLogger(loggerFactory)
    .Into(connectionCache);

// Create a hello proxy with the invocation pipeline as its invoker. Note that this proxy has no server address.
var hello = new HelloProxy(pipeline, new Uri("ice:/hello"));

Menu();
string? line = null;
do
{
    try
    {
        Console.Write("==> ");
        await Console.Out.FlushAsync();
        line = await Console.In.ReadLineAsync();

        switch (line)
        {
            case "t":
                // The locator interceptor calls the locator during this invocation to resolve `/hello` into one or more
                // server addresses; the locator interceptor caches successful resolutions.
                await hello.SayHelloAsync();
                break;
            case "s":
                await hello.ShutdownAsync();
                break;
            case "x":
                break;
            case "?":
                Menu();
                break;
            default:
                Console.WriteLine($"unknown command '{line}'");
                Menu();
                break;
        }
    }
    catch (Exception exception)
    {
        await Console.Error.WriteLineAsync(exception.ToString());
    }
}
while (line != "x");

await connectionCache.ShutdownAsync();

static void Menu() =>
    Console.WriteLine(
        "usage:\n" +
        "t: send greeting\n" +
        "s: shutdown server\n" +
        "x: exit\n" +
        "?: help\n");
