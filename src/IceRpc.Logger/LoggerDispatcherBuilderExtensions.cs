// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Extensions.DependencyInjection.Builder;
using IceRpc.Logger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IceRpc.Configure;

/// <summary>This class provides extension methods to add logger middleware to a <see cref="Router"/>.
/// </summary>
public static class LoggerDispatcherBuilderExtensions
{
    /// <summary>Adds a <see cref="LoggerMiddleware"/> to the dispatcher being configured.</summary>
    /// <param name="builder">The dispatcher builder.</param>
    /// <returns>The dispatcher builder.</returns>
    public static DispatcherBuilder UseLogger(this DispatcherBuilder builder) =>
        builder.Use(next =>
        new LoggerMiddleware(next, builder.ServiceProvider.GetRequiredService<ILoggerFactory>()));
}
