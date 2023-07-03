// Copyright (c) ZeroC, Inc.

using Demo;
using IceRpc;
using IceRpc.Ice;
using IceRpc.Retry;
using Microsoft.Extensions.Logging;

// Create an invocation pipeline for all our proxies.
var pipeline = new Pipeline();

// Create a locator proxy with the invocation pipeline as its invoker.
var locatorProxy = new LocatorProxy(pipeline, new Uri("ice://localhost/DemoIceGrid/Locator"));

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
    .UseLocator(locatorProxy, loggerFactory)
    .UseLogger(loggerFactory)
    .Into(connectionCache);

// Create a hello proxy with the invocation pipeline as its invoker. Note that this proxy has no server address.
var helloProxy = new HelloProxy(pipeline, new Uri("ice:/hello"));


// The locator interceptor calls the locator during this invocation to resolve `/hello` into one or more
// server addresses; the locator interceptor caches successful resolutions.
await helloProxy.SayHelloAsync();

// A second call would use the resolved server adress for `/hello` from the locator interceptor cache.
await helloProxy.SayHelloAsync();

await connectionCache.ShutdownAsync();
