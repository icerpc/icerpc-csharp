// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Extensions.DependencyInjection.Builder;

/// <summary>A builder for configuring IceRpc server dispatcher.</summary>
public class DispatcherBuilder : IDispatcherBuilder
{
    /// <inheritdoc/>
    public IServiceProvider ServiceProvider { get; }

    private readonly Router _router = new();

    /// <inheritdoc/>
    public IDispatcherBuilder Map<TService>(string path) where TService : class
    {
        _router.Map(path, (IDispatcher)ServiceProvider.GetRequiredService<TService>());
        return this;
    }

    /// <inheritdoc/>
    public IDispatcherBuilder Mount<TService>(string prefix) where TService : class
    {
        _router.Mount(prefix, (IDispatcher)ServiceProvider.GetRequiredService<TService>());
        return this;
    }

    /// <inheritdoc/>
    public IDispatcherBuilder Use(Func<IDispatcher, IDispatcher> middleware)
    {
        _router.Use(middleware);
        return this;
    }

    internal IDispatcher Build() => _router;

    internal DispatcherBuilder(IServiceProvider provider) => ServiceProvider = provider;
}
