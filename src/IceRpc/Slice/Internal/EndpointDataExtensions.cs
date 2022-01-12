// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Slice.Internal
{
    /// <summary>This class provides extension methods for <see cref="EndpointData"/>.</summary>
    internal static class EndpointDataExtensions
    {
        /// <summary>Converts an endpoint data into an endpoint. This method is used when decoding an endpoint.
        /// </summary>
        /// <param name="endpointData">The endpoint data struct.</param>
        /// <returns>The new endpoint.</returns>
        internal static Endpoint ToEndpoint(this in EndpointData endpointData) =>
            new(Protocol.FromString(endpointData.Protocol),
                string.IsInterned(endpointData.Transport) ?? endpointData.Transport,
                endpointData.Host,
                endpointData.Port,
                endpointData.Params.ToImmutableList());
    }
}
