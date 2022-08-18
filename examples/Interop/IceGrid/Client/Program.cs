// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using Microsoft.Extensions.Logging;

await using var connectionCache = new ConnectionCache(new ConnectionCacheOptions());

// Create a new invocation pipeline
var pipeline = new Pipeline();

// Create a locator proxy using the invocation pipeline.
var locator = new LocatorProxy(pipeline, new Uri("ice://localhost/DemoIceGrid/Locator"));

// Create a logger factory that logs to the console.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddFilter("IceRpc", LogLevel.Information);
        builder.AddSimpleConsole(configure => configure.IncludeScopes = true);
    });

// Create a hello proxy using the invocation pipeline. Note that this proxy has no server address.
var hello = new HelloProxy(pipeline, new Uri("ice:/hello"));

// Add the locator interceptor and logger interceptor to the pipeline
pipeline = pipeline.UseLocator(locator).UseLogger(loggerFactory).Into(connectionCache);

// Interactive prompt to the user
menu();
string? line = null;
do
{
    try
    {
        Console.Write("==> ");
        Console.Out.Flush();
        line = Console.In.ReadLine();

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
                Console.WriteLine("unknown command `" + line + "'");
                menu();
                break;
        };
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
    }
} while (line != "x");

static void menu()
{
    Console.WriteLine(
        "usage:\n" +
        "t: send greeting\n" +
        "s: shutdown server\n" +
        "x: exit\n" +
        "?: help\n");
}
