// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>Default implementation for <see cref="IServiceProviderFeature" />.</summary>
public sealed class ServiceProviderFeature : IServiceProviderFeature
{
    /// <inheritdoc/>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>Constructs a service provider feature.</summary>
    /// <param name="provider">The service provider held by this feature.</param>
    public ServiceProviderFeature(IServiceProvider provider) => ServiceProvider = provider;
}
