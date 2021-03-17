// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Interop
{
    /// <summary>Interop extension methods for IServicePrx.</summary>
    public static class Proxy
    {
        /// <summary>Returns the facet of this service proxy.</summary>
        /// <param name="proxy">The proxy.</param>
        /// <returns>The facet.</returns>
        public static string GetFacet(this IServicePrx proxy) => proxy.Impl.Facet;

        /// <summary>Returns the identity of this service proxy.</summary>
        /// <param name="proxy">The proxy.</param>
        /// <returns>The identity.</returns>
        public static Identity GetIdentity(this IServicePrx proxy) => proxy.Impl.Identity;
    }
}
