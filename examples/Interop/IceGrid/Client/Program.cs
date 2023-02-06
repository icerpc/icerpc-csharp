// Copyright (c) ZeroC, Inc.

using Demo;
using IceRpc;
using IceRpc.Retry;
using Microsoft.Extensions.Logging;

await using var connectionCache = new ConnectionCache();

// Create a new invocation pipeline
var pipeline = new Pipeline();

// Create a locator proxy with the invocation pipeline as its invoker.
var locator = new LocatorProxy(pipeline, new Uri("ice://localhost/DemoIceGrid/Locator"));

// Create a hello proxy with the invocation pipeline as its invoker. Note that this proxy has no server address.
var hello = new HelloProxy(pipeline, new Uri("ice:/hello"));

// Create a logger factory that logs to the console.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddFilter("IceRpc", LogLevel.Information);
        builder.AddSimpleConsole(configure => configure.IncludeScopes = true);
    });

// Add the retry, locator, and logger interceptors to the pipeline
pipeline = pipeline
    .UseRetry(new RetryOptions(), loggerFactory)
    .UseLocator(locator, loggerFactory)
    .UseLogger(loggerFactory)
    .Into(connectionCache);

// Interactive prompt to the user
menu();
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
                await hello.SayHelloAsync();
                break;
            case "s":
                await hello.ShutdownAsync();
                break;
            case "x":
                break;
            case "?":
                menu();
                break;
            default:
                Console.WriteLine($"unknown command '{line}'");
                menu();
                break;
        };
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync(ex.ToString());
    }
} while (line != "x");

static void menu() =>
    Console.WriteLine(
        "usage:\n" +
        "t: send greeting\n" +
        "s: shutdown server\n" +
        "x: exit\n" +
        "?: help\n"
    );
