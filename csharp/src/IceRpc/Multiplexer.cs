// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>Implements <see cref="IRouter"/> using two concurrent dictionaries.</summary>
    internal class Multiplexer : IRouter
    {
        public IDispatcher NotFound
        {
            get => _notFound;

            set
            {
                // The registration of any route, including the not found route, triggers the creation of the pipeline.
                _pipeline ??= CreatePipeline();
                _notFound = value;
            }
        }
        private readonly IDictionary<string, IDispatcher> _exactMatchRoutes =
            new ConcurrentDictionary<string, IDispatcher>();

        // The full prefix of this multiplexer, which includes its parent's full prefix. When null, this multiplexer is
        // a top-level multiplexer.
        private readonly string? _fullPrefix;

        private readonly List<Func<IDispatcher, IDispatcher>> _middlewareList = new();

        private IDispatcher _notFound =
            IDispatcher.FromInlineDispatcher(
                (current, cancel) => throw new ServiceNotFoundException(RetryPolicy.OtherReplica));

        private IDispatcher? _pipeline;

        private readonly IDictionary<string, IDispatcher> _prefixMatchRoutes =
            new ConcurrentDictionary<string, IDispatcher>();

        ValueTask<OutgoingResponseFrame> IDispatcher.DispatchAsync(Current current, CancellationToken cancel) =>
            (_pipeline ??= CreatePipeline()).DispatchAsync(current, cancel);

        public void Map(string path, IDispatcher dispatcher)
        {
            Check(path, nameof(path));
            _pipeline ??= CreatePipeline();
            _exactMatchRoutes[path] = dispatcher;
        }

        public void Mount(string prefix, IDispatcher dispatcher)
        {
            Check(prefix, nameof(prefix));
            prefix = RemoveOptionalTrailingSlash(prefix);

            _pipeline ??= CreatePipeline();
            _prefixMatchRoutes[prefix] = dispatcher;
        }

        public IRouter Route(string prefix, Action<IRouter> configure)
        {
            var subRouter = new Multiplexer(this, prefix);
            configure(subRouter);
            Mount(prefix, subRouter);
            return subRouter;
        }

        public bool Unmap(string path) => _exactMatchRoutes.Remove(path);
        public bool Unmount(string prefix) => _prefixMatchRoutes.Remove(prefix);

        public void Use(params Func<IDispatcher, IDispatcher>[] middleware)
        {
            if (_pipeline != null)
            {
                throw new InvalidOperationException("all middlewares must be registered before routes");
            }
            _middlewareList.AddRange(middleware);
        }

        public override string ToString() => _fullPrefix is string fullPrefix ? fullPrefix : "";

        internal Multiplexer()
        {
        }

        // TODO: temporary
        internal bool ContainsRoute(string path) => _exactMatchRoutes.ContainsKey(path);

        private static void Check(string s, string paramName)
        {
            if (s.Length == 0 || s[0] != '/')
            {
                throw new ArgumentException($"{paramName} must start with a /", paramName);
            }
        }

        // A prefix can have an optional trailing slash, e.g. `/foo/bar/`, which is equivalent to `/foo/bar` for a
        // prefix. This method normalizes the prefix by removing this trailing slash. Note that this method never
        // removes the leading slash and multiple trailing slashes are not removed - at most one trailing slash is
        // removed.
        private static string RemoveOptionalTrailingSlash(string prefix) =>
            prefix.Length > 1 && prefix[^1] == '/' ? prefix[..^1] : prefix;

        private Multiplexer(Multiplexer parent, string prefix)
        {
            Check(prefix, nameof(prefix));
            prefix = RemoveOptionalTrailingSlash(prefix);

            _fullPrefix = (parent._fullPrefix is string parentFullPrefix) ? parentFullPrefix + prefix : prefix;
        }

        private IDispatcher CreatePipeline()
        {
            // The last dispatcher of the pipeline:
            var pipeline = IDispatcher.FromInlineDispatcher(
                (current, cancel) =>
                {
                    string path = current.Path;

                    if (_fullPrefix?.Length > 1)
                    {
                        // Remove _fullPrefix from path

                        if (path.StartsWith(_fullPrefix, StringComparison.Ordinal))
                        {
                            path = path.Length == _fullPrefix.Length ? "/" : path[_fullPrefix.Length..];
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"received request for path `{path}' in multiplexer mounted at `{_fullPrefix}'");
                        }
                    }
                    // else _fullPrefix is null or "/" and there is nothing to remove

                    // First check for an exact match
                    if (_exactMatchRoutes.TryGetValue(path, out IDispatcher? dispatcher))
                    {
                        return dispatcher.DispatchAsync(current, cancel);
                    }
                    else
                    {
                        // Then a prefix match
                        while (true)
                        {
                            if (_prefixMatchRoutes.TryGetValue(current.Path, out dispatcher))
                            {
                                return dispatcher.DispatchAsync(current, cancel);
                            }

                            if (path == "/")
                            {
                                return NotFound.DispatchAsync(current, cancel);
                            }

                            // Cut last segment
                            int lastSlashPos = path.LastIndexOf('/');
                            if (lastSlashPos > 0)
                            {
                                path = path[..lastSlashPos];
                            }
                            else
                            {
                                path = "/";
                            }
                            // and try again with the new shorter prefix
                        }
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
