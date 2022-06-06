// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features;

/// <summary>The default implementation of <see cref="IServiceProviderFeature"/>.</summary>
public class ServiceProviderFeature : IServiceProviderFeature
{
    /// <inheritdoc/>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>Constructs a service provider feature.</summary>
    public ServiceProviderFeature(IServiceProvider provider) => ServiceProvider = provider;
}
