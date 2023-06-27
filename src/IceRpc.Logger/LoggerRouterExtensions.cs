// Copyright (c) ZeroC, Inc.

using IceRpc.Logger;
using Microsoft.Extensions.Logging;

namespace IceRpc;

/// <summary>Provides an extension method to add a logger middleware to a <see cref="Router" />.</summary>
public static class LoggerRouterExtensions
{
    /// <summary>Adds a <see cref="LoggerMiddleware" /> to this router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="loggerFactory">The logger factory used to create a <see cref="ILogger{TCategoryName}" /> for
    /// <see cref="LoggerMiddleware" />.</param>
    /// <returns>The router being configured.</returns>
    /// <example>
    /// The following code adds the logger middleware to the dispatch pipeline.
    /// <code source="../../docfx/examples/IceRpc.Logger.Examples/LoggerMiddlewareExamples.cs" region="UseLogger" lang="csharp" />
    /// </example>
    /// <seealso href="https://github.com/icerpc/icerpc-csharp/tree/main/examples/GreeterLog"/>
    public static Router UseLogger(this Router router, ILoggerFactory loggerFactory) =>
       router.Use(next => new LoggerMiddleware(next, loggerFactory.CreateLogger<LoggerMiddleware>()));
}
