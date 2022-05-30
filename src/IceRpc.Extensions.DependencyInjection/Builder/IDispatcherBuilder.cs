// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Extensions.DependencyInjection.Builder;

/// <summary>A builder for configuring IceRpc server dispatcher.</summary>
public interface IDispatcherBuilder
{
    /// <summary>The service provider used by the builder.</summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>Registers a route with a path. If there is an existing route at the same path, it is replaced.
    /// </summary>
    /// <param name="path">The path of this route. It must match exactly the path of the request. In particular, it
    /// must start with a <c>/</c>.</param>
    /// <typeparam name="TService">The type of the target dispatcher.</typeparam>
    /// <exception cref="FormatException">Thrown if <paramref name="path"/> is not a valid path.</exception>
    /// <returns>The builder.</returns>
    IDispatcherBuilder Map<TService>(string path) where TService : class;

    /// <summary>Registers a route with a prefix. If there is an existing route at the same prefix, it is replaced.
    /// </summary>
    /// <param name="prefix">The prefix of this route. This prefix will be compared with the start of the path of
    /// the request.</param>
    /// <typeparam name="TService">The type of the target service.</typeparam>
    /// <exception cref="FormatException">Thrown if <paramref name="prefix"/> is not a valid path.</exception>
    /// <returns>The builder.</returns>
    IDispatcherBuilder Mount<TService>(string prefix) where TService : class;

    /// <summary>Registers a middleware.</summary>
    /// <param name="middleware">The middleware to register.</param>
    /// <returns>The builder.</returns>
    IDispatcherBuilder Use(Func<IDispatcher, IDispatcher> middleware);
}
