// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Extensions.DependencyInjection.Internal;

/// <summary>The default implementation of <see cref="IServiceProviderFeature"/>.</summary>
internal class ServiceProviderFeature : IServiceProviderFeature
{
    /// <inheritdoc/>
    public IServiceProvider ServiceProvider { get; }

    internal ServiceProviderFeature(IServiceProvider provider) => ServiceProvider = provider;
}
