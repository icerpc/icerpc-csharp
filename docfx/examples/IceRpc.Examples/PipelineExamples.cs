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
        // Create a simple console logger factory and configure the log level for category IceRpc.
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            builder
                .AddSimpleConsole()
                .AddFilter("IceRpc", LogLevel.Information));

        // Create a client connection.
        await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

        // Create an invocation pipeline with the retry and logger interceptors.
        Pipeline pipeline = new Pipeline()
            .UseLogger(loggerFactory)
            .Into(connection);

        // Create a proxy that uses the pipeline as its invoker.
        var greeterProxy = new GreeterProxy(pipeline);
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
