// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.Logging;

namespace IceRpc.Logger.Examples;

// This class provides code snippets used by the doc-comments of the logger interceptor.
public static class LoggerInterceptorExamples
{
    public static async Task UseLogger()
    {
        #region UseLogger
        // Create a simple console logger factory and configure the log level for category IceRpc.
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            builder
                .AddSimpleConsole()
                .AddFilter("IceRpc", LogLevel.Debug));

        // Create a client connection that logs messages to a logger with category IceRpc.ClientConnection.
        await using var connection = new ClientConnection(
            new Uri("icerpc://localhost"),
            logger: loggerFactory.CreateLogger<ClientConnection>());

        // Create an invocation pipeline and install the logger interceptor. This interceptor logs invocations using
        // category `IceRpc.Logger.LoggerInterceptor`.
        Pipeline pipeline = new Pipeline()
            .UseLogger(loggerFactory)
            .Into(connection);
        #endregion
    }
}
