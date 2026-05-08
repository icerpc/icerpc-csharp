// Copyright (c) ZeroC, Inc.

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides the mechanism to configure a dispatcher when using Dependency Injection (DI). Each request is
/// dispatched in its own scope.</summary>
/// <remarks>The per-request DI scope is disposed as soon as the dispatcher returns its
/// <see cref="OutgoingResponse" />. The protocol connection then reads <c>response.Payload</c> (and
/// <c>response.PayloadContinuation</c>) to send the response on the wire — at which point the scope is already gone.
/// As a result, a response payload that lazily pulls from a scoped service (for example a streamed reply backed by
/// a scoped <c>HttpClient</c> or by EF Core) will encounter an <see cref="ObjectDisposedException" /> partway through
/// transmission. To avoid this, materialize the response data inside the dispatcher (under the scope) — for example
/// by reading it into a buffer or pipe before returning the response — rather than relying on lazy reads from a
/// scoped dependency.</remarks>
public interface IDispatcherBuilder
{
    /// <summary>Gets the service provider.</summary>
    /// <value>The <see cref="IServiceProvider" />.</value>
    IServiceProvider ServiceProvider { get; }

    /// <summary>Registers a route with a path. If there is an existing route at the same path, it is replaced.
    /// </summary>
    /// <typeparam name="TService">The type of the DI service that will handle the requests. The implementation of this
    /// service must implement <see cref="IDispatcher" />.</typeparam>
    /// <param name="path">The path of this route. It must match exactly the path of the request. In particular, it
    /// must start with a <c>/</c>.</param>
    /// <exception cref="FormatException">Thrown if <paramref name="path" /> is not a valid path.</exception>
    /// <returns>This builder.</returns>
    /// <remarks>With Slice, it is common for <typeparamref name="TService" /> to correspond to a generated
    /// I{name}Service interface. This generated interface does not extend <see cref="IDispatcher" />.</remarks>
    IDispatcherBuilder Map<TService>(string path) where TService : notnull;

    /// <summary>Registers a route with a prefix. If there is an existing route at the same prefix, it is replaced.
    /// </summary>
    /// <typeparam name="TService">The type of the DI service that will handle the requests. The implementation of this
    /// service must implement <see cref="IDispatcher" />.</typeparam>
    /// <param name="prefix">The prefix of this route. This prefix will be compared with the start of the path of
    /// the request.</param>
    /// <exception cref="FormatException">Thrown if <paramref name="prefix" /> is not a valid path.</exception>
    /// <returns>This builder.</returns>
    /// <remarks>With Slice, it is common for <typeparamref name="TService" /> to correspond to a generated
    /// I{name}Service interface. This generated interface does not extend <see cref="IDispatcher" />.</remarks>
    IDispatcherBuilder Mount<TService>(string prefix) where TService : notnull;

    /// <summary>Creates a sub-router, configures this sub-router and mounts it at the given <c>prefix</c>.</summary>
    /// <param name="prefix">The prefix of the route to the sub-router.</param>
    /// <param name="configure">A delegate that configures the new sub-router.</param>
    void Route(string prefix, Action<IDispatcherBuilder> configure);

    /// <summary>Registers a middleware.</summary>
    /// <param name="middleware">The middleware to register.</param>
    /// <returns>This builder.</returns>
    IDispatcherBuilder Use(Func<IDispatcher, IDispatcher> middleware);
}
