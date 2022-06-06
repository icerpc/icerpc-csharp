// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Builder.Internal;

/// <summary>Provides the default implementation of <see cref="IDispatcherBuilder"/>.</summary>
internal class DispatcherBuilder : IDispatcherBuilder
{
    /// <inheritdoc/>
    public IServiceProvider ServiceProvider { get; }

    private readonly Router _router = new();

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
    public IDispatcherBuilder Use(Func<IDispatcher, IDispatcher> middleware)
    {
        _router.Use(middleware);
        return this;
    }

    internal DispatcherBuilder(IServiceProvider provider) => ServiceProvider = provider;

    internal IDispatcher Build() => new InlineDispatcher(async (request, cancel) =>
    {
        AsyncServiceScope asyncScope = ServiceProvider.CreateAsyncScope();
        await using var _ = asyncScope.ConfigureAwait(false);

        request.Features = request.Features.With<IServiceProviderFeature>(
            new ServiceProviderFeature(asyncScope.ServiceProvider));

        return await _router.DispatchAsync(request, cancel).ConfigureAwait(false);
    });
}
