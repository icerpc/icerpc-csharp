// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Locator;

await using var connectionCache = new ConnectionCache(new ConnectionCacheOptions());

// Create a proxy to the IceGrid Locator
var locator = new LocatorProxy(connectionCache, new Uri("ice://localhost/DemoIceGrid/Locator"));

// Add the locator interceptor to the pipeline
IInvoker pipeline = new Pipeline().UseLocator(locator).Into(connectionCache);

// Create a hello proxy using the invocation pipeline. Note that this proxy has no server address.
var hello = new HelloProxy(pipeline, new Uri("ice:/hello"));

Console.WriteLine("Sending a greeting to the server...");

await hello.SayHelloAsync();

Console.WriteLine("The server received the greeting");
