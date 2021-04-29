// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using System.Collections.Generic;
namespace IceRpc
{
    public interface IProxyFactory<T> where T : class, IServicePrx
    {
        /// <summary>Creates a new service proxy.</summary>
        /// <param name="path">The path of the target service.</param>
        /// <param name="protocol">The protocol of the new proxy.</param>
        /// <param name="encoding">The encoding of the new proxy. Usually corresponds to the protocol's encoding.
        /// </param>
        /// <param name="endpoint">The endpoint of the proxy (can be null).</param>
        /// <param name="altEndpoints">The alternative endpoints of the proxy (can be empty).</param>
        /// <param name="connection">The connection of the proxy (can be null).</param>
        /// <param name="options">The service proxy options.</param>
        /// <returns>The new service proxy.</returns>
        T Create(
            string path,
            Protocol protocol,
            Encoding encoding,
            Endpoint? endpoint,
            IEnumerable<Endpoint> altEndpoints,
            Connection? connection,
            ProxyOptions options);

        /// <summary>Creates a new service proxy that uses the ice1 protocol.</summary>
        /// <param name="identity">The identity of the target service.</param>
        /// <param name="facet">The facet of the target service.</param>
        /// <param name="encoding">The encoding of the new proxy. Usually corresponds to the protocol's encoding.
        /// </param>
        /// <param name="endpoint">The endpoint of the proxy (can be null).</param>
        /// <param name="altEndpoints">The alternative endpoints of the proxy (can be empty).</param>
        /// <param name="connection">The connection of the proxy (can be null).</param>
        /// <param name="options">The service proxy options.</param>
        /// <returns>The new service proxy.</returns>
        T Create(
            Identity identity,
            string facet,
            Encoding encoding,
            Endpoint? endpoint,
            IEnumerable<Endpoint> altEndpoints,
            Connection? connection,
            ProxyOptions options);
    }
}
