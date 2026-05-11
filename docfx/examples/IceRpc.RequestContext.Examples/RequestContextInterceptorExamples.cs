// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc.Features;
using System.Collections.Immutable;
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
            new RequestContextFeature(new Dictionary<string, string>
            {
                ["UserId"] = Environment.UserName.ToLowerInvariant(),
                ["MachineName"] = Environment.MachineName
            }.ToImmutableDictionary()));
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
        // An interceptor that adds an entry to the request context. Since IRequestContextFeature.Value is
        // read-only, the pattern is to retrieve the existing entries as an ImmutableDictionary, add the new
        // entry to produce a new ImmutableDictionary, then install a new feature wrapping the result.
        _ = new Pipeline()
            .Use(next => new InlineInvoker((request, cancellationToken) =>
            {
                IRequestContextFeature? feature = request.Features.Get<IRequestContextFeature>();
                ImmutableDictionary<string, string> dictionary =
                    feature?.Value.ToImmutableDictionary() ?? ImmutableDictionary<string, string>.Empty;
                dictionary = dictionary.Add("CorrelationId", Guid.NewGuid().ToString());
                request.Features = request.Features.With<IRequestContextFeature>(
                    new RequestContextFeature(dictionary));
                return next.InvokeAsync(request, cancellationToken);
            }))
            .UseRequestContext();
        #endregion
    }
}
