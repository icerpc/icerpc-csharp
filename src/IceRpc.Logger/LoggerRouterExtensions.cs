// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Logger;
using Microsoft.Extensions.Logging;

namespace IceRpc;

/// <summary>This class provides extension methods to add logger middleware to a <see cref="Router"/>.
/// </summary>
public static class LoggerRouterExtensions
{
    /// <summary>Adds a <see cref="LoggerMiddleware"/> to the router.</summary>
    /// <param name="router">The router being configured.</param>
    /// <param name="loggerFactory">The logger factory used to create the logger.</param>
    /// <returns>The router being configured.</returns>
    public static Router UseLogger(this Router router, ILoggerFactory loggerFactory) =>
        router.Use(next => new LoggerMiddleware(next, loggerFactory));
}
