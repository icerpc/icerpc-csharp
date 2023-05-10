// Copyright (c) ZeroC, Inc.

using GreeterExample;

namespace IceRpc.Examples;

public static class RouterExamples
{
    public static async Task CreatingAndUsingTheRouter()
    {
        #region CreatingAndUsingTheRouter
        // Create a router that maps two services, each using its own default service path.
        Router router = new Router()
            .Map<IGreeterService>(new Chatbot())
            .Map<IGreeterServiceAdmin>(new ChatbotAdmin());

        // Create a server that uses the router as its dispatcher.
        await using var server = new Server(router);
        server.Listen();
        #endregion
    }

    public static async Task CreatingAndUsingTheRouterWithMiddleware()
    {
        #region CreatingAndUsingTheRouterWithMiddleware
        // Create a simple console logger factory and configure the log level for category IceRpc.
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            builder
                .AddSimpleConsole()
                .AddFilter("IceRpc", LogLevel.Debug));

        // Create a router that maps two services, each using its own default service path.
        Router router = new Router()
            .UseLogger(loggerFactory)
            .UseCompressor(CompressionFormat.Brotli)
            .Map<IGreeterService>(new Chatbot())
            .Map<IGreeterServiceAdmin>(new ChatbotAdmin());

        // Create a server that uses the router as its dispatcher.
        await using var server = new Server(router);
        server.Listen();
        #endregion
    }

    public static async Task CreatingAndUsingTheRouterWithAnInlineDispatcher()
    {
        #region CreatingAndUsingTheRouter
        // Create a router that maps two services, each using its own default service path.
        Router router = new Router()
            .Map<IGreeterService>(new Chatbot())
            .Map<IGreeterServiceAdmin>(new ChatbotAdmin());

        // Create a server that uses the router as its dispatcher.
        await using var server = new Server(router);
        server.Listen();
        #endregion
    }
}
