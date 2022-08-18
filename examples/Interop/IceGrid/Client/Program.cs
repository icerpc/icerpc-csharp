// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

await using var connectionCache = new ConnectionCache(new ConnectionCacheOptions());

// Create a proxy to the IceGrid Locator
var locator = new LocatorProxy(connectionCache, new Uri("ice://localhost/DemoIceGrid/Locator"));

// Add the locator interceptor to the pipeline
IInvoker pipeline = new Pipeline().UseLocator(locator).Into(connectionCache);

// Create a hello proxy using the invocation pipeline. Note that this proxy has no server address.
var hello = new HelloProxy(pipeline, new Uri("ice:/hello"));

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
                await hello.SayHelloAsync();
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
