// Copyright (c) ZeroC, Inc.

using GreeterExample;
using Microsoft.Extensions.Logging;

namespace IceRpc.Logger.Examples;

// This class provides code snippets used by the doc-comments of the logger middleware.
public static class LoggerMiddlewareExamples
{
    public static void UseLogger()
    {
        #region UseLogger
        // Create a simple console logger factory and configure the log level for category IceRpc.
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            builder
                .AddSimpleConsole()
                .AddFilter("IceRpc", LogLevel.Debug));

        // Create a router (dispatch pipeline) and install the logger middleware. This middleware logs dispatches
        // using category `IceRpc.Logger.LoggerMiddleware`.
        Router router = new Router()
            .UseLogger(loggerFactory)
            .Map<IGreeterService>(new Chatbot());

        #endregion
    }
}
