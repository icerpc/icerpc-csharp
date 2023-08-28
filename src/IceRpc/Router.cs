// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Provides methods for routing incoming requests to dispatchers.</summary>
/// <example>
/// The following example shows how you would install a middleware, and map a service.
/// <code source="../../docfx/examples/IceRpc.Examples/RouterExamples.cs" region="CreatingAndUsingTheRouterWithMiddleware" lang="csharp" />
/// You can easily create your own middleware and add it to the router. The next example shows how you can create a
/// middleware using an <see cref="InlineDispatcher"/> and add it to the router with
/// <see cref="Use(Func{IDispatcher, IDispatcher})"/>.
/// <code source="../../docfx/examples/IceRpc.Examples/RouterExamples.cs" region="CreatingAndUsingTheRouterWithAnInlineDispatcher" lang="csharp" />
/// </example>
/// <remarks><para>The <see cref="Router"/> class allows you to define a dispatch pipeline for customizing how incoming
/// requests are processed. You utilize the Router class for creating routes with various middleware, sub-routers,
/// and dispatchers.</para>
/// <para>Incoming requests flow through the dispatch pipeline. An incoming request is first processed by the router's
/// middleware and then routed to a target dispatcher based on its path. The target dispatcher returns an outgoing
/// response that goes through the dispatch pipeline in the opposite direction.</para>
/// <para>The routing algorithm determines how incoming requests are routed to dispatchers. The router first checks if
/// a dispatcher is registered with the request's path, which corresponds to dispatchers registered using
/// <see cref="Map(string, IDispatcher)"/>. If there isn't a dispatcher registered for the request's path, the router
/// looks for dispatchers registered with a matching prefix, which corresponds to dispatchers installed using
/// <see cref="Mount(string, IDispatcher)"/>. When searching for a matching prefix, the router starts with the request
/// path and successively tries chopping segments from the end of the path until either the path is exhausted or a
/// dispatcher matching the prefix is found. Finally, if the router cannot find any dispatcher, it returns an
/// <see cref="OutgoingResponse"/> with a <see cref="StatusCode.NotFound"/> status code.</para></remarks>
public sealed class Router : IDispatcher
{
    /// <summary>Gets the absolute path-prefix of this router. The absolute path of a service added to this
    /// Router is: <c>$"{AbsolutePrefix}{path}"</c> where <c>path</c> corresponds to the argument given to
    /// <see cref="Map(string, IDispatcher)" />.</summary>
    /// <value>The absolute prefix of this router. It is either an empty string or a string with two or more
    /// characters starting with a <c>/</c>.</value>
    public string AbsolutePrefix { get; } = "";

    // When searching in the prefixMatchRoutes, we search up to MaxSegments before giving up. This prevents a
    // a malicious client from sending a request with a huge number of segments (/a/a/a/a/a/a/a/a/a/a...) that
    // results in numerous unsuccessful lookups.
    private const int MaxSegments = 10;

    private readonly Lazy<IDispatcher> _dispatcher;
    private readonly Dictionary<string, IDispatcher> _exactMatchRoutes = new();

    private readonly Stack<Func<IDispatcher, IDispatcher>> _middlewareStack = new();

    private readonly Dictionary<string, IDispatcher> _prefixMatchRoutes = new();

    /// <summary>Constructs a top-level router.</summary>
    public Router() => _dispatcher = new Lazy<IDispatcher>(CreateDispatchPipeline);

    /// <summary>Constructs a router with an absolute prefix.</summary>
    /// <param name="absolutePrefix">The absolute prefix of the new router. It must start with a <c>/</c>.</param>
    /// <exception cref="FormatException">Thrown if <paramref name="absolutePrefix" /> is not a valid path.
    /// </exception>
    public Router(string absolutePrefix)
        : this()
    {
        ServiceAddress.CheckPath(absolutePrefix);
        absolutePrefix = NormalizePrefix(absolutePrefix);
        AbsolutePrefix = absolutePrefix.Length > 1 ? absolutePrefix : "";
    }

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        CancellationToken cancellationToken = default) =>
        _dispatcher.Value.DispatchAsync(request, cancellationToken);

    /// <summary>Registers a route with a path. If there is an existing route at the same path, it is replaced.
    /// </summary>
    /// <param name="path">The path of this route. It must match exactly the path of the request. In particular, it
    /// must start with a <c>/</c>.</param>
    /// <param name="dispatcher">The target of this route. It is typically a service.</param>
    /// <returns>This router.</returns>
    /// <exception cref="FormatException">Thrown if <paramref name="path" /> is not a valid path.</exception>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync" /> was already
    /// called on this router.</exception>
    /// <seealso cref="Mount" />
    public Router Map(string path, IDispatcher dispatcher)
    {
        if (_dispatcher.IsValueCreated)
        {
            throw new InvalidOperationException(
                $"Cannot call {nameof(Map)} after calling {nameof(IDispatcher.DispatchAsync)}.");
        }
        ServiceAddress.CheckPath(path);
        _exactMatchRoutes[path] = dispatcher;
        return this;
    }

    /// <summary>Registers a route with a prefix. If there is an existing route at the same prefix, it is replaced.
    /// </summary>
    /// <param name="prefix">The prefix of this route. This prefix will be compared with the start of the path of
    /// the request.</param>
    /// <param name="dispatcher">The target of this route.</param>
    /// <returns>This router.</returns>
    /// <exception cref="FormatException">Thrown if <paramref name="prefix" /> is not a valid path.</exception>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync" /> was already
    /// called on this router.</exception>
    /// <seealso cref="Map(string, IDispatcher)" />
    public Router Mount(string prefix, IDispatcher dispatcher)
    {
        if (_dispatcher.IsValueCreated)
        {
            throw new InvalidOperationException(
                $"Cannot call {nameof(Mount)} after calling {nameof(IDispatcher.DispatchAsync)}.");
        }
        ServiceAddress.CheckPath(prefix);
        prefix = NormalizePrefix(prefix);
        _prefixMatchRoutes[prefix] = dispatcher;
        return this;
    }

    /// <summary>Installs a middleware in this router. A middleware must be installed before calling
    /// <see cref="IDispatcher.DispatchAsync" />.</summary>
    /// <param name="middleware">The middleware to install.</param>
    /// <returns>This router.</returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync" /> was already
    /// called on this router.</exception>
    public Router Use(Func<IDispatcher, IDispatcher> middleware)
    {
        if (_dispatcher.IsValueCreated)
        {
            throw new InvalidOperationException(
                $"All middleware must be registered before calling {nameof(IDispatcher.DispatchAsync)}.");
        }
        _middlewareStack.Push(middleware);
        return this;
    }

    /// <summary>Returns a string that represents this router.</summary>
    /// <returns>A string that represents this router.</returns>
    public override string ToString() => AbsolutePrefix.Length > 0 ? $"router({AbsolutePrefix})" : "router";

    // Trim trailing slashes but keep the leading slash.
    private static string NormalizePrefix(string prefix)
    {
        if (prefix.Length > 1)
        {
            prefix = prefix.TrimEnd('/');
            if (prefix.Length == 0)
            {
                prefix = "/";
            }
        }
        return prefix;
    }

    private IDispatcher CreateDispatchPipeline()
    {
        // The last dispatcher of the pipeline:
        IDispatcher dispatchPipeline = new InlineDispatcher(
            (request, cancellationToken) =>
            {
                string path = request.Path;

                if (AbsolutePrefix.Length > 0)
                {
                    // Remove AbsolutePrefix from path. AbsolutePrefix starts with a '/' but usually does not end with
                    // one.
                    path = path.StartsWith(AbsolutePrefix, StringComparison.Ordinal) ?
                        (path.Length == AbsolutePrefix.Length ? "/" : path[AbsolutePrefix.Length..]) :
                        throw new InvalidOperationException(
                            $"Received request for path '{path}' in router mounted at '{AbsolutePrefix}'.");
                }
                // else there is nothing to remove

                // First check for an exact match
                if (_exactMatchRoutes.TryGetValue(path, out IDispatcher? dispatcher))
                {
                    return dispatcher.DispatchAsync(request, cancellationToken);
                }
                else
                {
                    // Then a prefix match
                    string prefix = NormalizePrefix(path);

                    foreach (int _ in Enumerable.Range(0, MaxSegments))
                    {
                        if (_prefixMatchRoutes.TryGetValue(prefix, out dispatcher))
                        {
                            return dispatcher.DispatchAsync(request, cancellationToken);
                        }

                        if (prefix == "/")
                        {
                            return new(new OutgoingResponse(request, StatusCode.NotFound));
                        }

                        // Cut last segment
                        int lastSlashPos = prefix.LastIndexOf('/');
                        prefix = lastSlashPos > 0 ? NormalizePrefix(prefix[..lastSlashPos]) : "/";
                        // and try again with the new shorter prefix
                    }
                    return new(new OutgoingResponse(request, StatusCode.InvalidData, "Too many segments in path."));
                }
            });

        foreach (Func<IDispatcher, IDispatcher> middleware in _middlewareStack)
        {
            dispatchPipeline = middleware(dispatchPipeline);
        }
        return dispatchPipeline;
    }
}
