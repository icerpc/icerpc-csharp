// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IceRpc.Extensions.DependencyInjection.Builder;

/// <summary>A builder for configuring IceRpc server dispatcher.</summary>
public class DispatcherBuilder : IDispatcherBuilder
{
    /// <summary>The service provider used by the builder.</summary>
    public IServiceProvider ServiceProvider { get; }

    List<Action<Router>> _configure = new();

    /// <inheritdoc/>
    public DispatcherBuilder Map<T>(string path) where T : IDispatcher
    {
        _configure.Add(router => router.Map(path, ActivatorUtilities.CreateInstance<T>(ServiceProvider)));
        return this;
    }

    /// <inheritdoc/>
    public DispatcherBuilder Map<TService, TDispatcher>()
        where TService : class
        where TDispatcher : IDispatcher
    {
        _configure.Add(
            router => router.Map<TService>(ActivatorUtilities.CreateInstance<TDispatcher>(ServiceProvider)));
        return this;
    }

    /// <inheritdoc/>
    public DispatcherBuilder Mount<T>(string prefix) where T : IDispatcher
    {
        _configure.Add(router => router.Mount(prefix, ActivatorUtilities.CreateInstance<T>(ServiceProvider)));
        return this;
    }

    /// <inheritdoc/>
    public DispatcherBuilder UseMiddleware<T>() where T : IDispatcher
    {
        _configure.Add(
            router => router.Use(next => ActivatorUtilities.CreateInstance<T>(
                ServiceProvider,
                new object[] { next })));
        return this;
    }

    /// <inheritdoc/>
    public DispatcherBuilder UseMiddleware<T, TOptions>()
        where T : IDispatcher
        where TOptions : class
    {
        _configure.Add(router =>
        {
            TOptions options = ServiceProvider.GetRequiredService<IOptions<TOptions>>().Value;
            router.Use(next => ActivatorUtilities.CreateInstance<T>(ServiceProvider, new object[] { next, options }));
        });
        return this;
    }

    internal IDispatcher Build()
    {
        var router = new Router();
        foreach(Action<Router> configure in _configure)
        {
            configure(router);
        }
        return router;
    }

    internal DispatcherBuilder(IServiceProvider provider) => ServiceProvider = provider;
}
