// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
namespace IceRpc.Interop
{
    /// <summary>Extension methods that give access to facets.</summary>
    public static class Facet
    {
        /// <summary>Returns the facet of this service proxy.</summary>
        /// <param name="proxy">The proxy.</param>
        /// <returns>The facet.</returns>
        public static string GetFacet(this IServicePrx proxy) => proxy.Impl.Facet;

        /// <summary>Returns the facet carried by this incoming request frame.</summary>
        /// <param name="request">The incoming request frame.</param>
        /// <returns>The facet.</returns>
        public static string GetFacet(this IncomingRequestFrame request) => request.Facet;

        /// <summary>Returns the facet carried by this outgoing request frame.</summary>
        /// <param name="request">The outgoing request frame.</param>
        /// <returns>The facet.</returns>
        public static string GetFacet(this OutgoingRequestFrame request) => request.Facet;

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
        /// <param name="proxy">The proxy being copied.</param>
        /// <param name="facet">The new facet.</param>
        /// <returns>A proxy with the specified facet and type.</returns>
        public static T WithFacet<T>(this IServicePrx proxy, string facet) where T : class, IServicePrx
        {
            if (facet == proxy.GetFacet() && proxy is T t)
            {
                return t;
            }
            else if (proxy.Protocol == Protocol.Ice1)
            {
                var options = (InteropServicePrxOptions)proxy.Impl.CloneOptions();
                options.Facet = facet;
                return Proxy.GetFactory<T>().Create(options);
            }
            else
            {
                throw new ArgumentException($"cannot change the facet of an {proxy.Protocol.GetName()} proxy",
                                            nameof(proxy));
            }
        }
    }
}
