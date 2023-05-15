// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc.Features;

namespace IceRpc.RequestContext.Examples;

// This class provides code snippets used by the doc-comments of the request context interceptor.
public static class RequestContextInterceptorExamples
{
    public static async Task UseRequestContext()
    {
        #region UseRequestContext
        // Create a client connection.
        await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

        // Add the request context interceptor to the invocation pipeline.
        Pipeline pipeline = new Pipeline()
            .UseRequestContext()
            .Into(connection);

        var greeterProxy = new GreeterProxy(pipeline);

        // Create a feature collection holding an IRequestContextFeature.
        IFeatureCollection features = new FeatureCollection().With<IRequestContextFeature>(
            new RequestContextFeature
            {
                ["UserId"] = Environment.UserName.ToLowerInvariant(),
                ["MachineName"] = Environment.MachineName
            });

        // The request context interceptor encodes the request context feature into the request context field.
        string greeting = await greeterProxy.GreetAsync(Environment.UserName, features);
        #endregion
    }
}
