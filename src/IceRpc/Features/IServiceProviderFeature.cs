// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>A feature that gives access to the service provider. This service provider is typically the service
/// provider of the async scope created for the request.</summary>
/// <remarks>You should only use this feature in infrastructure code, such as an implementation of
/// <see cref="Builder.IDispatcherBuilder" />. Any other use falls into the Locator anti-pattern.</remarks>
public interface IServiceProviderFeature
{
    /// <summary>Gets the service provider.</summary>
    /// <value>The <see cref="IServiceProvider" />.</value>
    IServiceProvider ServiceProvider { get; }
}
