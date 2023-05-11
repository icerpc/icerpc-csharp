// Copyright (c) ZeroC, Inc.

using GreeterExample;

namespace IceRpc.Examples;

public static class RouterExamples
{
    public static async Task CreatingAndUsingTheRouter()
    {
        #region CreatingAndUsingTheRouter
        // Create a router and add a route for a service using its default service path.
        Router router = new Router()
            .Map<IGreeterService>(new Chatbot());

        // Create a server that uses the router as its dispatch pipeline.
        await using var server = new Server(router);
        server.Listen();
        #endregion
    }

    public static async Task CreatingAndUsingTheRouterWithMiddleware()
    {
        #region CreatingAndUsingTheRouterWithMiddleware
        // Create a router that uses the compressor middleware, and add a route for a service using its default service
        // path.
        Router router = new Router()
            .UseCompressor(CompressionFormat.Brotli)
            .Map<IGreeterService>(new Chatbot());

        // Create a server that uses the router as its dispatch pipeline.
        await using var server = new Server(router);
        server.Listen();
        #endregion
    }

    public static async Task CreatingAndUsingTheRouterWithAnInlineDispatcher()
    {
        #region CreatingAndUsingTheRouterWithAnInlineDispatcher
        // Create a router that uses a custom middleware defined as an inline dispatcher, and add a route for a service
        // using its default service path.
        Router router = new Router()
            .Use(next => new InlineDispatcher(async (request, cancellationToken) =>
            {
                Console.WriteLine("before _next.DispatchAsync");
                OutgoingResponse response = await next.DispatchAsync(request, cancellationToken);
                Console.WriteLine($"after _next.DispatchAsync; the response status code is {response.StatusCode}");
                return response;
            }))
            .Map<IGreeterService>(new Chatbot());

        // Create a server that uses the router as its dispatcher.
        await using var server = new Server(router);
        server.Listen();
        #endregion
    }
}
