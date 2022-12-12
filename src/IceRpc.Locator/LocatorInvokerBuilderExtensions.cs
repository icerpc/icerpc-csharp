// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Locator;
using Microsoft.Extensions.Logging;

namespace IceRpc.Builder;

/// <summary>This class provides extension methods to install the locator interceptor in an
/// <see cref="IInvokerBuilder" />.</summary>
public static class LocatorInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="LocatorInterceptor" /> to the builder. This interceptor relies on the
    /// <see cref="LocatorLocationResolver" /> service managed by the service provider.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseLocator(this IInvokerBuilder builder)
    {
        if (builder.ServiceProvider.GetService(typeof(LocatorLocationResolver)) is not ILocationResolver locationResolver)
        {
            throw new InvalidOperationException(
                $"Could not find service of type '{nameof(LocatorLocationResolver)}' in the service container");
        }

        if (builder.ServiceProvider.GetService(typeof(ILogger<LocatorInterceptor>)) is not ILogger logger)
        {
            throw new InvalidOperationException(
                $"Could not find service of type '{nameof(ILogger<LocatorInterceptor>)}' in the service container.");
        }

        return builder.Use(next => new LocatorInterceptor(next, locationResolver, logger));
    }
}
