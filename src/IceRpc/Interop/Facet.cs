// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc.Interop
{
    /// <summary>Extension methods that give access to facets.</summary>
    public static class Facet
    {
        /// <summary>Returns the facet of this proxy.</summary>
        /// <param name="proxy">The proxy.</param>
        /// <returns>The facet.</returns>
        public static string GetFacet(this Proxy proxy) => proxy.Facet;

        /// <summary>Returns the facet carried by this incoming request frame.</summary>
        /// <param name="request">The incoming request frame.</param>
        /// <returns>The facet.</returns>
        public static string GetFacet(this IncomingRequest request) =>
            request.FacetPath.Count == 0 ? "" : request.FacetPath[0];

        /// <summary>Returns the facet carried by this outgoing request frame.</summary>
        /// <param name="request">The outgoing request frame.</param>
        /// <returns>The facet.</returns>
        public static string GetFacet(this OutgoingRequest request) =>
            request.FacetPath.Count == 0 ? "" : request.FacetPath[0];

        /// <summary>Returns the facet of this exception.</summary>
        /// <param name="exception">The exception.</param>
        /// <returns>The facet.</returns>
        public static string GetFacet(this OperationNotFoundException exception) => exception.Facet;

        /// <summary>Returns the facet of this exception.</summary>
        /// <param name="exception">The exception.</param>
        /// <returns>The facet.</returns>
        public static string GetFacet(this ServiceNotFoundException exception) => exception.Facet;

        /// <summary>Creates a copy of this proxy with a new facet.</summary>
        /// <param name="proxy">The source proxy.</param>
        /// <param name="facet">The new facet.</param>
        /// <returns>A new proxy with the specified facet.</returns>
        public static Proxy WithFacet(this Proxy proxy, string facet)
        {
            if (proxy.Protocol == Protocol.Ice1)
            {
                var newProxy = proxy.Clone();
                newProxy.Facet = facet;
                // keeps endpoint, connection, invoker etc.
                return newProxy;
            }
            else
            {
                throw new ArgumentException($"cannot change the facet of an {proxy.Protocol.GetName()} proxy",
                                            nameof(proxy));
            }
        }
    }
}
