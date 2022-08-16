// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Locator;

await using var connectionCache = new ConnectionCache(new ConnectionCacheOptions { });

// Create a proxy to the IceGrid locator
var locator = new LocatorProxy(connectionCache, new Uri("ice://localhost:4061/DemoIceGrid/Locator"));

// Add the locator interceptor to the pipeline
IInvoker pipeline = new Pipeline().UseLocator(locator).Into(connectionCache);

// Create a hello proxy using the invocation pipeline
var hello = new HelloProxy(pipeline, new Uri("ice:/hello"));

Console.WriteLine("Sending a greeting to the server...");

await hello.SayHelloAsync();

Console.WriteLine("The server received the greeting");
