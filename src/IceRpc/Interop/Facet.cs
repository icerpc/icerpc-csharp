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

     /// <summary>Returns the facet of this proxy.</summary>
        /// <param name="prx">The typed proxy.</param>
        /// <returns>The facet.</returns>
        public static string GetFacet(this IPrx prx) => prx.Proxy.Facet;

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

        /// <summary>Creates a copy of this proxy with a new facet and type.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="prx">The source proxy.</param>
        /// <param name="facet">The new facet.</param>
        /// <returns>A proxy with the specified facet and type.</returns>
        public static T WithFacet<T>(this IPrx prx, string facet) where T : IPrx, new()
        {
            if (facet == prx.Proxy.Facet && prx is T t)
            {
                return t;
            }
            else if (prx.Proxy.Protocol == Protocol.Ice1)
            {
                T newPrx = new T { Proxy = prx.Proxy.Clone() };
                newPrx.Proxy.Facet = facet;
                // keeps endpoint, connection, invoker etc.
                return newPrx;
            }
            else
            {
                throw new ArgumentException($"cannot change the facet of an {prx.Proxy.Protocol.GetName()} proxy",
                                            nameof(prx));
            }
        }
    }
}
