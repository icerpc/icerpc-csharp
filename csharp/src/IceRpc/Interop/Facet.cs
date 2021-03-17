// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Interop
{
    /// <summary>Extension methods that give access to facets.</summary>
    public static class Facet
    {
        /// <summary>Returns the facet of this service proxy.</summary>
        /// <param name="proxy">The proxy.</param>
        /// <returns>The facet.</returns>
        public static string GetFacet(this IServicePrx proxy) => proxy.Impl.Facet;
        public static string GetFacet(this IncomingRequestFrame request) => request.Facet;
        public static string GetFacet(this OutgoingRequestFrame request) => request.Facet;

        /// <summary>Returns the facet of this exception.</summary>
        /// <param name="exception">The exception.</param>
        /// <returns>The facet.</returns>
        public static string GetFacet(this OperationNotFoundException exception) => exception.Facet;

        /// <summary>Returns the facet of this exception.</summary>
        /// <param name="exception">The exception.</param>
        /// <returns>The facet.</returns>
        public static string GetFacet(this ServiceNotFoundException exception) => exception.Facet;
    }
}
