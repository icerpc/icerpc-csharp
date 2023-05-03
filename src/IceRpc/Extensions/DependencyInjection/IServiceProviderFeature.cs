// Copyright (c) ZeroC, Inc.

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>A feature that gives access to the service provider. When transmitted with an
/// <see cref="IncomingRequest" />, this service provider is typically the service provider of the async scope created
/// for the request.</summary>
public interface IServiceProviderFeature
{
    /// <summary>Gets the service provider.</summary>
    /// <value>The <see cref="IServiceProvider" />.</value>
    IServiceProvider ServiceProvider { get; }
}
