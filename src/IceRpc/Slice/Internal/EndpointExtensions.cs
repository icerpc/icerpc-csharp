// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>This class provides extension methods for <see cref="Endpoint"/>.</summary>
    internal static class EndpointExtensions
    {
        /// <summary>Converts this endpoint into an endpoint data. This method is called when encoding an endpoint.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>An endpoint data with all the properties of this endpoint.</returns>
        internal static EndpointData ToEndpointData(this Endpoint endpoint) =>
            new(endpoint.Protocol.Name, endpoint.Transport, endpoint.Host, endpoint.Port, endpoint.Params);
    }
}
