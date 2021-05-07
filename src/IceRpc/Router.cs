// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A router routes incoming requests to dispatchers.</summary>
    public sealed class Router : IDispatcher
    {
        /// <summary>Returns the absolute path-prefix of this router. The absolute path of a service added to this
        /// Router is <code>$"{AbsolutePrefix}{path}"</code> where <c>path</c> corresponds to the argument given to
        /// <see cref="Map"/>.</summary>
        /// <value>The absolute prefix of this router. It is either an empty string or a string with two or more
        /// characters starting with a <c>/</c>.</value>
        public string AbsolutePrefix { get; } = "";

        // When searching in the prefixMatchRoutes, we search up to MaxSegments before giving up. This prevents a
        // a malicious client from sending a request with a huge number of segments (/a/a/a/a/a/a/a/a/a/a...) that
        // results in numerous unsuccessful lookups.
        private const int MaxSegments = 10;

        private readonly IDictionary<string, IDispatcher> _exactMatchRoutes =
            new ConcurrentDictionary<string, IDispatcher>();

        private readonly List<Func<IDispatcher, IDispatcher>> _middlewareList = new();

        private IDispatcher? _pipeline;

        private readonly IDictionary<string, IDispatcher> _prefixMatchRoutes =
            new ConcurrentDictionary<string, IDispatcher>();

        /// <summary>Constructs a top-level router.</summary>
        public Router()
        {
        }

        /// <summary>Constructs a router with an absolute prefix.</summary>
        /// <param name="absolutePrefix">The absolute prefix of the new router. It must start with a <c>/</c>.</param>
        public Router(string absolutePrefix)
        {
            Internal.UriParser.CheckPath(absolutePrefix, nameof(absolutePrefix));
            absolutePrefix = NormalizePrefix(absolutePrefix);
            AbsolutePrefix = absolutePrefix.Length > 1 ? absolutePrefix : "";
        }

        /// <inherit-doc/>
        ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel) =>
            (_pipeline ??= CreatePipeline()).DispatchAsync(request, cancel);

        /// <summary>Registers a route with a path. If there is an existing route at the same path, it is replaced.
        /// </summary>
        /// <param name="path">The path of this route. It must match exactly the path of the request. In particular, it
        /// must start with a <c>/</c>.</param>
        /// <param name="dispatcher">The target of this route. It is typically an <see cref="IService"/>.</param>
        /// <exception name="ArgumentException">Raised if path does not start with a <c>/</c>.</exception>
        /// <seealso cref="Mount"/>
        public void Map(string path, IDispatcher dispatcher)
        {
            Internal.UriParser.CheckPath(path, nameof(path));
            _pipeline ??= CreatePipeline();
            _exactMatchRoutes[path] = dispatcher;
        }

        /// <summary>Registers a route to a service that uses the service default path as the route path. If there is
        /// an existing route at the same path, it is replaced.</summary>
        /// <param name="service">The target service of this route.</param>
        /// <seealso cref="Mount"/>
        public void Map<TService>(TService service) where TService : IService
        {
            if (typeof(TService).GetInterfaces().Where(
                    t => t.IsAssignableTo(typeof(IService))).FirstOrDefault() is Type serviceType &&
                serviceType.GetField("DefaultPath",
                                     BindingFlags.Public | BindingFlags.Static) is FieldInfo defaultPathField &&
                defaultPathField.GetValue(null) is string defaultPath)
            {
                _pipeline ??= CreatePipeline();
                _exactMatchRoutes[defaultPath] = service;
            }
            else
            {
                throw new InvalidOperationException(
                    $"{typeof(TService).FullName} does not have a string field named DefaultPath");
            }
        }

        /// <summary>Registers a route with a prefix. If there is an existing route at the same prefix, it is replaced.
        /// </summary>
        /// <param name="prefix">The prefix of this route. This prefix will be compared with the start of the path of
        /// the request.</param>
        /// <param name="dispatcher">The target of this route.</param>
        /// <exception name="ArgumentException">Raised if prefix does not start with a <c>/</c>.</exception>
        /// <seealso cref="Map"/>
        public void Mount(string prefix, IDispatcher dispatcher)
        {
            Internal.UriParser.CheckPath(prefix, nameof(prefix));
            prefix = NormalizePrefix(prefix);

            _pipeline ??= CreatePipeline();
            _prefixMatchRoutes[prefix] = dispatcher;
        }

        /// <summary>Creates a sub-router, configures this sub-router and mounts it (with <see cref="Mount"/>"/> at the
        /// given <c>prefix</c>.</summary>
        /// <param name="prefix">The prefix of the route to the sub-router.</param>
        /// <param name="configure">A delegate that configures the new sub-router.</param>
        /// <returns>The new sub-router.</returns>
        /// <exception name="ArgumentException">Raised if prefix does not start with a <c>/</c>.</exception>
        public Router Route(string prefix, Action<Router> configure)
        {
            Internal.UriParser.CheckPath(prefix, nameof(prefix));
            var subRouter = new Router($"{AbsolutePrefix}{prefix}");
            configure(subRouter);
            Mount(prefix, subRouter);
            return subRouter;
        }

        /// <summary>Unregisters a route previously registered with <see cref="Map"/>.</summary>
        /// <param name="path">The path of the route.</param>
        /// <returns>True when the route was found and unregistered; otherwise, false.</returns>
        /// <exception name="ArgumentException">Raised if path does not start with a <c>/</c>.</exception>
        public bool Unmap(string path)
        {
            Internal.UriParser.CheckPath(path, nameof(path));
            return _exactMatchRoutes.Remove(path);
        }

        /// <summary>Unregisters a route previously registered with <see cref="Mount"/>.</summary>
        /// <param name="prefix">The prefix of the route.</param>
        /// <returns>True when the route was found and unregistered; otherwise, false.</returns>
        /// <exception name="ArgumentException">Raised if prefix does not start with a <c>/</c>.</exception>
        public bool Unmount(string prefix)
        {
            Internal.UriParser.CheckPath(prefix, nameof(prefix));
            return _prefixMatchRoutes.Remove(prefix);
        }

        /// <summary>Installs one or more middleware in this router. Middlewares must be installed before any route is
        /// registered.</summary>
        /// <param name="middleware">One or more middlewares.</param>
        /// <exception name="InvalidOperationException">Raised if a route was already registered, or if
        /// <see cref="IDispatcher.DispatchAsync"/> was called on this router.</exception>
        public void Use(params Func<IDispatcher, IDispatcher>[] middleware)
        {
            if (_pipeline != null)
            {
                throw new InvalidOperationException("all middlewares must be registered before routes");
            }
            _middlewareList.AddRange(middleware);
        }

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

        private IDispatcher CreatePipeline()
        {
            // The last dispatcher of the pipeline:
            IDispatcher pipeline = new InlineDispatcher(
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
                                throw new ServiceNotFoundException(RetryPolicy.OtherReplica);
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
                                throw new ServiceNotFoundException(RetryPolicy.OtherReplica);
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
                        throw new ServerException("too many segments in path");
                    }
                });

            IEnumerable<Func<IDispatcher, IDispatcher>> middlewareEnumerable = _middlewareList;
            foreach (Func<IDispatcher, IDispatcher> middleware in middlewareEnumerable.Reverse())
            {
                pipeline = middleware(pipeline);
            }
            return pipeline;
        }
    }
}
