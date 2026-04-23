// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc.Features;
using VisitorCenter;

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

        // Create a feature collection holding an IRequestContextFeature.
        IFeatureCollection features = new FeatureCollection().With<IRequestContextFeature>(
            new RequestContextFeature
            {
                ["UserId"] = Environment.UserName.ToLowerInvariant(),
                ["MachineName"] = Environment.MachineName
            });
        #endregion

        {
        #region UseRequestContextWithSliceProxy
        // The request context interceptor encodes the request context feature into the request
        // context field.
        var greeter = new GreeterProxy(pipeline);
        string greeting = await greeter.GreetAsync(Environment.UserName, features);
        #endregion
        }

        {
        #region UseRequestContextWithProtobufClient
        // The request context interceptor encodes the request context feature into the request
        // context field.
        var greeter = new GreeterClient(pipeline);
        GreetResponse response = await greeter.GreetAsync(
            new GreetRequest { Name = Environment.UserName }, features);
        #endregion
        }
    }

    public static void UpdateRequestContextInInterceptor()
    {
        #region UpdateRequestContextInInterceptor
        // An interceptor that adds an entry to the request context. The appropriate pattern is
        // copy-on-write: build a new dictionary and install a new feature, rather than mutating the
        // existing Value in place (which would race with the outgoing encoding).
        Pipeline pipeline = new Pipeline()
            .Use(next => new InlineInvoker((request, cancellationToken) =>
            {
                IRequestContextFeature? feature = request.Features.Get<IRequestContextFeature>();
                IDictionary<string, string> current =
                    feature?.Value ?? new Dictionary<string, string>();
                var updated = new Dictionary<string, string>(current)
                {
                    ["CorrelationId"] = Guid.NewGuid().ToString()
                };
                request.Features = request.Features.With<IRequestContextFeature>(
                    new RequestContextFeature(updated));
                return next.InvokeAsync(request, cancellationToken);
            }))
            .UseRequestContext();
        #endregion
    }
}
