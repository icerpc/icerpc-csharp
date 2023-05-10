// Copyright (c) ZeroC, Inc.

using GreeterExample;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace IceRpc.Examples;

public static class PipelineExamples
{
    public static async Task CreatingAndUsingThePipeline()
    {
        #region CreatingAndUsingThePipeline
        // Create a connection cache.
        await using var connectionCache = new ConnectionCache();

        // Create a simple console logger factory and configure the log level for category IceRpc.
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            builder
                .AddSimpleConsole()
                .AddFilter("IceRpc", LogLevel.Information));

        // Create an invocation pipeline with the retry and logger interceptors.
        Pipeline pipeline = new Pipeline()
            .UseRetry()
            .UseLogger(loggerFactory)
            .Into(connectionCache);

        // Create a proxy that uses the pipeline as its invoker.
        var greeterProxy = new GreeterProxy(
            pipeline,
            new ServiceAddress(new Uri("icerpc://host1/greeter"))
            {
                AltServerAddresses = new[]
                {
                    new ServerAddress(new Uri("icerpc://host2/greeter")),
                    new ServerAddress(new Uri("icerpc://host3/greeter")),
                }.ToImmutableList()
            });
        #endregion
    }

    public static void UseWithInlineInterceptor()
    {
        #region UseWithInlineInterceptor
        Pipeline pipeline = new Pipeline()
            .Use(next => new InlineInvoker(async (request, cancel) =>
            {
                // Add some logic before processing the request
                Console.WriteLine("before _next.InvokeAsync");
                // Class the next invoker on the invocation pipeline.
                IncomingResponse response = await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                Console.WriteLine($"after _next.InvokerAsync; the response status code is {response.StatusCode}");
                // Add some logic after receiving the response.
                return response;
            }));
        #endregion
    }
}
