// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Features;
using IceRpc.RequestContext;

await using var connection = new ClientConnection("icerpc://127.0.0.1");

// Add the request context interceptor to the invocation pipeline.
var pipeline = new Pipeline().UseRequestContext().Into(connection);

var hello = new HelloPrx(connection);
hello.Proxy.Invoker = pipeline;

Console.Write("To say hello to the server, type your name: ");

if (Console.ReadLine() is string name)
{
    var features = new FeatureCollection();
    // Add the request context feature to the request features for the SayHello invocation.
    features.Set<IRequestContextFeature>(new RequestContextFeature
        {
            Value = new Dictionary<string, string>
            {
                ["UserId"] = name.ToLowerInvariant(),
                ["MachineName"] = Environment.MachineName
            }
        });
    Console.WriteLine(await hello.SayHelloAsync(name, features));
}
