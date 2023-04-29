// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using RequestContextExample;

await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

// Add the request context interceptor to the invocation pipeline.
Pipeline pipeline = new Pipeline().UseRequestContext().Into(connection);

var greeter = new GreeterProxy(pipeline);

var features = new FeatureCollection();

// Set the request context feature in features.
features.Set<IRequestContextFeature>(
    new RequestContextFeature
    {
        Value = new Dictionary<string, string>
        {
            ["UserId"] = Environment.UserName.ToLowerInvariant(),
            ["MachineName"] = Environment.MachineName
        }
    });

// The request context interceptor encodes the request context feature into the request context field.
string greeting = await greeter.GreetAsync(Environment.UserName, features);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
