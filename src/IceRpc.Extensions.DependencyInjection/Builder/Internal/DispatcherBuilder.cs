// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;

namespace IceRpc.Builder.Internal;

/// <summary>Provides the default implementation of <see cref="IDispatcherBuilder" />.</summary>
internal class DispatcherBuilder : IDispatcherBuilder
{
    /// <inheritdoc/>
    public IServiceProvider ServiceProvider { get; }

    private readonly Router _router;

    /// <inheritdoc/>
    public IDispatcherBuilder Map<TService>(string path) where TService : notnull
    {
        _router.Map(path, new ServiceAdapter<TService>());
        return this;
    }

    /// <inheritdoc/>
    public IDispatcherBuilder Mount<TService>(string prefix) where TService : notnull
    {
        _router.Mount(prefix, new ServiceAdapter<TService>());
        return this;
    }

    /// <inheritdoc/>
    public void Route(string prefix, Action<IDispatcherBuilder> configure) =>
        _router.Route(prefix, router => configure(new DispatcherBuilder(router, ServiceProvider)));

    /// <inheritdoc/>
    public IDispatcherBuilder Use(Func<IDispatcher, IDispatcher> middleware)
    {
        _router.Use(middleware);
        return this;
    }

    internal DispatcherBuilder(IServiceProvider provider)
        : this(new Router(), provider)
    {
    }

    private DispatcherBuilder(Router router, IServiceProvider provider)
    {
        _router = router;
        ServiceProvider = provider;
    }

    internal IDispatcher Build() => new InlineDispatcher(async (request, cancellationToken) =>
    {
        AsyncServiceScope asyncScope = ServiceProvider.CreateAsyncScope();
        await using ConfiguredAsyncDisposable _ = asyncScope.ConfigureAwait(false);

        request.Features = request.Features.With<IServiceProviderFeature>(
            new ServiceProviderFeature(asyncScope.ServiceProvider));

        return await _router.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
    });
}
