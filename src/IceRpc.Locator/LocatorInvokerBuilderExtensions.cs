// Copyright (c) ZeroC, Inc.

using IceRpc.Locator;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>This class provides extension methods to install the locator interceptor in an
/// <see cref="IInvokerBuilder" />.</summary>
public static class LocatorInvokerBuilderExtensions
{
    /// <summary>Adds a <see cref="LocatorInterceptor" /> to the builder. This interceptor relies on the
    /// <see cref="LocatorLocationResolver" /> service managed by the service provider.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder being configured.</returns>
    public static IInvokerBuilder UseLocator(this IInvokerBuilder builder) =>
        builder.ServiceProvider.GetService(typeof(LocatorLocationResolver)) is ILocationResolver locationResolver ?
        builder.Use(next => new LocatorInterceptor(next, locationResolver)) :
        throw new InvalidOperationException(
            $"Could not find service of type '{nameof(LocatorLocationResolver)}' in the service container");
}
