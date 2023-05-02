// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;

namespace IceRpc.Builder.Internal;

/// <summary>Implements <see cref="IDispatcherBuilder" /> for Microsoft's DI container.</summary>
internal sealed class DispatcherBuilder : IDispatcherBuilder
{
    public IServiceProvider ServiceProvider { get; }

    private readonly Router _router;

    public IDispatcherBuilder Map<TService>(string path) where TService : notnull
    {
        _router.Map(path, new ServiceAdapter<TService>());
        return this;
    }

    public IDispatcherBuilder Mount<TService>(string prefix) where TService : notnull
    {
        _router.Mount(prefix, new ServiceAdapter<TService>());
        return this;
    }

    public void Route(string prefix, Action<IDispatcherBuilder> configure) =>
        _router.Route(prefix, router => configure(new DispatcherBuilder(router, ServiceProvider)));

    public IDispatcherBuilder Use(Func<IDispatcher, IDispatcher> middleware)
    {
        _router.Use(middleware);
        return this;
    }

    internal DispatcherBuilder(IServiceProvider provider)
        : this(new Router(), provider)
    {
    }

    internal IDispatcher Build() => new InlineDispatcher(async (request, cancellationToken) =>
    {
        AsyncServiceScope asyncScope = ServiceProvider.CreateAsyncScope();
        await using ConfiguredAsyncDisposable _ = asyncScope.ConfigureAwait(false);

        request.Features = request.Features.With<IServiceProviderFeature>(
            new ServiceProviderFeature(asyncScope.ServiceProvider));

        return await _router.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
    });

    private DispatcherBuilder(Router router, IServiceProvider provider)
    {
        _router = router;
        ServiceProvider = provider;
    }
}
