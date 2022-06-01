// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>A feature that gives access to the service provider. This service provider is typically the service
/// provider of the async scope created for the request.</summary>
public interface IServiceProviderFeature
{
    /// <summary>Gets the service provider.</summary>
    IServiceProvider ServiceProvider { get; }
}
