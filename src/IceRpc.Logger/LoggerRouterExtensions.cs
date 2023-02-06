// Copyright (c) ZeroC, Inc.

using IceRpc.Logger;
using Microsoft.Extensions.Logging;

namespace IceRpc;

/// <summary>This class provides extension methods to add the logger middleware to a <see cref="Router" />.
/// </summary>
public static class LoggerRouterExtensions
{
    /// <summary>Adds a <see cref="LoggerMiddleware" /> to this router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="logger">The logger to log to.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseLogger(this Router router, ILogger logger) =>
        router.Use(next => new LoggerMiddleware(next, logger));

    /// <summary>Adds a <see cref="LoggerMiddleware" /> to this router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="loggerFactory">The logger factory used to create a <see cref="ILogger{TCategoryName}" /> for
    /// <see cref="LoggerMiddleware" />.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseLogger(this Router router, ILoggerFactory loggerFactory) =>
       router.UseLogger(loggerFactory.CreateLogger<LoggerMiddleware>());
}
