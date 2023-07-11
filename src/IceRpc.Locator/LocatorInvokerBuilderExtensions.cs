// Copyright (c) ZeroC, Inc.

using IceRpc.Ice;
using IceRpc.Locator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>This class provides extension methods to add the locator interceptor to a <see cref="IInvokerBuilder" />.
/// </summary>
public static class LocatorInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="LocatorInterceptor" /> to the builder. This interceptor relies on the
    /// <see cref="ILocator"/> and <see cref="ILoggerFactory" /> services managed by the service provider.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="options">The options to configure the <see cref="LocatorInterceptor" />.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseLocator(this IInvokerBuilder builder, LocatorOptions options)
    {
        if (builder.ServiceProvider.GetService(typeof(ILocator)) is not ILocator locator)
        {
            throw new InvalidOperationException(
                $"Could not find service of type '{nameof(ILocator)}' in the service container.");
        }

        var logger = (ILogger?)builder.ServiceProvider.GetService(typeof(ILogger<LocatorInterceptor>));
        return builder.Use(
            next => new LocatorInterceptor(
                next,
                new LocatorLocationResolver(locator, options, logger ?? NullLogger.Instance)));
    }

    /// <summary>Adds a <see cref="LocatorInterceptor" /> to the builder. This interceptor relies on the
    /// <see cref="ILocationResolver" /> service managed by the service provider.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseLocator(this IInvokerBuilder builder) =>
        builder.ServiceProvider.GetService(typeof(ILocationResolver)) is ILocationResolver locationResolver ?
        builder.Use(next => new LocatorInterceptor(next, locationResolver)) :
        throw new InvalidOperationException(
            $"Could not find service of type '{nameof(ILocationResolver)}' in the service container.");
}
