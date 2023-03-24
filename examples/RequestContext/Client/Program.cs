// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using IceRpc.RequestContext;
using RequestContextExample;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Add the request context interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline().UseRequestContext().Into(connection);

var hello = new HelloProxy(pipeline);

var features = new FeatureCollection();
// Add the request context feature to the request features for the SayHello invocation.
features.Set<IRequestContextFeature>(new RequestContextFeature
    {
        Value = new Dictionary<string, string>
        {
            ["UserId"] = Environment.UserName.ToLowerInvariant(),
            ["MachineName"] = Environment.MachineName
        }
    });

string greeting = await hello.SayHelloAsync(Environment.UserName, features);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
