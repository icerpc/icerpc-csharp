// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Ice;
using IceRpc.Retry;
using Microsoft.Extensions.Logging;
using VisitorCenter;

// Create an invocation pipeline for all our proxies.
var pipeline = new Pipeline();

// Create a locator proxy with the invocation pipeline as its invoker.
var locator = new LocatorProxy(pipeline, new Uri("ice://localhost/IceGrid/Locator"));

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

// Create a greeter proxy with the invocation pipeline as its invoker. Note that this proxy has no server address.
var greeter = new GreeterProxy(pipeline, new Uri("ice:/greeter"));

// Send a request to the remote object and get the response.
// The locator interceptor calls the locator during this invocation to resolve `/greeter` into one or more
// server addresses; the locator interceptor caches successful resolutions.
string greeting = await greeter.GreetAsync(Environment.UserName);
Console.WriteLine(greeting);

// Send another request to the remote object to demonstrate that the locator interceptor caches the server address
// resolved during the previous invocation.
greeting = await greeter.GreetAsync("alice");
Console.WriteLine(greeting);

await connectionCache.ShutdownAsync();
