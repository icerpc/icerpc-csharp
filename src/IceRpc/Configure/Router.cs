// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc.Configure
{
    /// <summary>A router routes incoming requests to dispatchers.</summary>
    public sealed class Router : IDispatcher
    {
        /// <summary>Returns the absolute path-prefix of this router. The absolute path of a service added to this
        /// Router is <code>$"{AbsolutePrefix}{path}"</code> where <c>path</c> corresponds to the argument given to
        /// <see cref="Map(string, IDispatcher)"/>.</summary>
        /// <value>The absolute prefix of this router. It is either an empty string or a string with two or more
        /// characters starting with a <c>/</c>.</value>
        public string AbsolutePrefix { get; } = "";

        // When searching in the prefixMatchRoutes, we search up to MaxSegments before giving up. This prevents a
        // a malicious client from sending a request with a huge number of segments (/a/a/a/a/a/a/a/a/a/a...) that
        // results in numerous unsuccessful lookups.
        private const int MaxSegments = 10;

        private readonly IDictionary<string, IDispatcher> _exactMatchRoutes = new Dictionary<string, IDispatcher>();

        private ImmutableList<Func<IDispatcher, IDispatcher>> _middlewareList =
            ImmutableList<Func<IDispatcher, IDispatcher>>.Empty;

        private IDispatcher? _dispatcher;

        private readonly IDictionary<string, IDispatcher> _prefixMatchRoutes = new Dictionary<string, IDispatcher>();

        /// <summary>Constructs a top-level router.</summary>
        public Router()
        {
        }

        /// <summary>Constructs a router with an absolute prefix.</summary>
        /// <param name="absolutePrefix">The absolute prefix of the new router. It must start with a <c>/</c>.</param>
        /// <exception cref="FormatException">Thrown if <paramref name="absolutePrefix"/> is not a valid path.
        /// </exception>
        public Router(string absolutePrefix)
        {
            Proxy.CheckPath(absolutePrefix);
            absolutePrefix = NormalizePrefix(absolutePrefix);
            AbsolutePrefix = absolutePrefix.Length > 1 ? absolutePrefix : "";
        }

        ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel) =>
            (_dispatcher ??= CreateDispatchPipeline()).DispatchAsync(request, cancel);

        /// <summary>Registers a route with a path. If there is an existing route at the same path, it is replaced.
        /// </summary>
        /// <param name="path">The path of this route. It must match exactly the path of the request. In particular, it
        /// must start with a <c>/</c>.</param>
        /// <param name="dispatcher">The target of this route. It is typically an <see cref="IService"/>.</param>
        /// <exception cref="FormatException">Thrown if <paramref name="path"/> is not a valid path.</exception>
        /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync"/> was already
        /// called on this router.</exception>
        /// <seealso cref="Mount"/>
        public void Map(string path, IDispatcher dispatcher)
        {
            if (_dispatcher != null)
            {
                throw new InvalidOperationException(
                    $"cannot call {nameof(Map)} after calling {nameof(IDispatcher.DispatchAsync)}");
            }
            Proxy.CheckPath(path);
            _exactMatchRoutes[path] = dispatcher;
        }

        /// <summary>Registers a route to a service that uses the service default path as the route path. If there is
        /// an existing route at the same path, it is replaced.</summary>
        /// <typeparam name="T">The service type used to get the default path.</typeparam>
        /// <param name="service">The target service of this route.</param>
        /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync"/> was already
        /// called on this router.</exception>
        /// <seealso cref="Mount"/>
        public void Map<T>(IDispatcher service) where T : class => Map(typeof(T).GetDefaultPath(), service);

        /// <summary>Registers a route with a prefix. If there is an existing route at the same prefix, it is replaced.
        /// </summary>
        /// <param name="prefix">The prefix of this route. This prefix will be compared with the start of the path of
        /// the request.</param>
        /// <param name="dispatcher">The target of this route.</param>
        /// <exception cref="FormatException">Thrown if <paramref name="prefix"/> is not a valid path.</exception>
        /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync"/> was already
        /// called on this router.</exception>
        /// <seealso cref="Map(string, IDispatcher)"/>
        public void Mount(string prefix, IDispatcher dispatcher)
        {
            if (_dispatcher != null)
            {
                throw new InvalidOperationException(
                    $"cannot call {nameof(Mount)} after calling {nameof(IDispatcher.DispatchAsync)}");
            }
            Proxy.CheckPath(prefix);
            prefix = NormalizePrefix(prefix);
            _prefixMatchRoutes[prefix] = dispatcher;
        }

        /// <summary>Creates a sub-router, configures this sub-router and mounts it (with <see cref="Mount"/>) at the
        /// given <c>prefix</c>.</summary>
        /// <param name="prefix">The prefix of the route to the sub-router.</param>
        /// <param name="configure">A delegate that configures the new sub-router.</param>
        /// <returns>The new sub-router.</returns>
        /// <exception cref="FormatException">Thrown if <paramref name="prefix"/> is not a valid path.</exception>
        public Router Route(string prefix, Action<Router> configure)
        {
            Proxy.CheckPath(prefix);
            var subRouter = new Router($"{AbsolutePrefix}{prefix}");
            configure(subRouter);
            Mount(prefix, subRouter);
            return subRouter;
        }

        /// <summary>Installs one or more middleware in this router. A middleware must be installed before calling
        /// <see cref="IDispatcher.DispatchAsync"/>.</summary>
        /// <param name="middleware">One or more middleware.</param>
        /// <exception cref="InvalidOperationException">Thrown if <see cref="IDispatcher.DispatchAsync"/> was already
        /// called on this router.</exception>
        public Router Use(params Func<IDispatcher, IDispatcher>[] middleware)
        {
            if (_dispatcher != null)
            {
                throw new InvalidOperationException(
                    $"all middleware must be registered before calling {nameof(IDispatcher.DispatchAsync)}");
            }
            _middlewareList = _middlewareList.AddRange(middleware);
            return this;
        }

        /// <inheritdoc/>
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
                (request, cancel) =>
                {
                    string path = request.Path;

                    if (AbsolutePrefix.Length > 0)
                    {
                        // Remove AbsolutePrefix from path

                        if (path.StartsWith(AbsolutePrefix, StringComparison.Ordinal))
                        {
                            if (path.Length == AbsolutePrefix.Length)
                            {
                                // We consume everything so there is nothing left to match.
                                return ConnectionOptions.DefaultDispatcher.DispatchAsync(request, cancel);
                            }
                            else
                            {
                                path = path[AbsolutePrefix.Length..];
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"received request for path '{path}' in router mounted at '{AbsolutePrefix}'");
                        }
                    }
                    // else there is nothing to remove

                    Debug.Assert(path.Length > 0);

                    // First check for an exact match
                    if (_exactMatchRoutes.TryGetValue(path, out IDispatcher? dispatcher))
                    {
                        return dispatcher.DispatchAsync(request, cancel);
                    }
                    else
                    {
                        // Then a prefix match
                        string prefix = NormalizePrefix(path);

                        foreach (int _ in Enumerable.Range(0, MaxSegments))
                        {
                            if (_prefixMatchRoutes.TryGetValue(prefix, out dispatcher))
                            {
                                return dispatcher.DispatchAsync(request, cancel);
                            }

                            if (prefix == "/")
                            {
                                return ConnectionOptions.DefaultDispatcher.DispatchAsync(request, cancel);
                            }

                            // Cut last segment
                            int lastSlashPos = prefix.LastIndexOf('/');
                            if (lastSlashPos > 0)
                            {
                                prefix = NormalizePrefix(prefix[..lastSlashPos]);
                            }
                            else
                            {
                                prefix = "/";
                            }
                            // and try again with the new shorter prefix
                        }
                        throw new DispatchException("too many segments in path", DispatchErrorCode.InvalidData);
                    }
                });

            IEnumerable<Func<IDispatcher, IDispatcher>> middlewareEnumerable = _middlewareList;
            foreach (Func<IDispatcher, IDispatcher> middleware in middlewareEnumerable.Reverse())
            {
                dispatchPipeline = middleware(dispatchPipeline);
            }
            return dispatchPipeline;
        }
    }
}
