// Copyright (c) ZeroC, Inc.

using GreeterExample;
using IceRpc.Retry;
using Microsoft.Extensions.Logging;

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
            .UseRetry(
                new RetryOptions { MaxAttempts = 5 },
                loggerFactory)
            .UseLogger(loggerFactory)
            .Into(connectionCache);

        // Create a proxy that uses the pipeline.
        IGreeter greeterProxy = new GreeterProxy(pipeline);
        #endregion
    }

    public static void UseWithInlineInvoker()
    {
        #region UseWithInlineInvoker
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
