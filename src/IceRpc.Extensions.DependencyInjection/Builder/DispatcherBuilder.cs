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
    public IDispatcherBuilder Map<T>(string path) where T : class
    {
        _router.Map(path, (IDispatcher)ActivatorUtilities.CreateInstance<T>(ServiceProvider));
        return this;
    }

    /// <inheritdoc/>
    public IDispatcherBuilder Mount<T>(string prefix) where T : class
    {
        _router.Mount(prefix, (IDispatcher)ActivatorUtilities.CreateInstance<T>(ServiceProvider));
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
