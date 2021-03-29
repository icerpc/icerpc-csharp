// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc
{
    /// <summary>Routes requests to a dispatcher.</summary>
    public interface IRouter : IDispatcher
    {
        /// <summary>Creates a new top-level router using the default router implementation.</summary>
        /// <returns>A new top-level router.</returns>
        public static IRouter CreateDefault() => new Multiplexer();

        /// <summary>Gets or sets the dispatcher for the "not found" route. This dispatcher is called when no route is
        /// found for a request.</summary>
        /// <value>The dispatcher for the "not found" route.</value>
        IDispatcher NotFound { get; set; }

        /// <summary>Registers a route with a path. If there is an existing route at the same path, it is replaced.
        /// </summary>
        /// <param name="path">The path of this route. It must match exactly the path of the request. In particular, it
        /// must start with a <c>/</c>.</param>
        /// <param name="dispatcher">The target of this route. It is typically a <see cref="IService"/>.</param>
        /// <seealso cref="Mount"/>
        void Map(string path, IDispatcher dispatcher);

        /// <summary>Registers a route with a prefix. If there is an existing route at the same prefix, it is replaced.
        /// </summary>
        /// <param name="prefix">The prefix of this route. This prefix will be compared with the start of the path of
        /// the request.</param>
        /// <param name="dispatcher">The target of this route.</param>
        /// <seealso cref="Map"/>
        void Mount(string prefix, IDispatcher dispatcher);

        /// <summary>Creates a sub-router, configures this sub-router and mounts it (with <see cref="Mount"/>"/> at the
        /// given <c>prefix</c>.</summary>
        /// <param name="prefix">The prefix of the route to the sub-router.</param>
        /// <param name="configure">A delegate that configures the new sub-router.</param>
        /// <returns>The new sub-router.</returns>
        IRouter Route(string prefix, Action<IRouter> configure);

        /// <summary>Unregisters a route previously registered with <see cref="Map"/>.</summary>
        /// <param name="path">The path of the route.</param>
        /// <returns>True when the route was found and unregistered; otherwise, false.</returns>
        bool Unmap(string path);

        /// <summary>Unregisters a route previously registered with <see cref="Mount"/>.</summary>
        /// <param name="prefix">The prefix of the route.</param>
        /// <returns>True when the route was found and unregistered; otherwise, false.</returns>
        bool Unmount(string prefix);

        /// <summary>Installs one or more middleware in this router. Middlewares must be installed before any route is
        /// registered.</summary>
        /// <param name="middleware">One or more middleware.</param>
        void Use(params Func<IDispatcher, IDispatcher>[] middleware);
    }
}
