// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>The default implementation of <see cref="IServiceProviderFeature" />.</summary>
public class ServiceProviderFeature : IServiceProviderFeature
{
    /// <inheritdoc/>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>Constructs a service provider feature.</summary>
    /// <param name="provider">The service provider hold by this feature.</param>
    public ServiceProviderFeature(IServiceProvider provider) => ServiceProvider = provider;
}
