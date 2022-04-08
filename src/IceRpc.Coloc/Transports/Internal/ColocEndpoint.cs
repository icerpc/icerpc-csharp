// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Sockets;

namespace IceRpc.Transports
{
    internal class ColocEndpoint : System.Net.EndPoint
    {
        private readonly Endpoint _endpoint;

        /// <inheritdoc/>
        public override AddressFamily AddressFamily => AddressFamily.Unspecified;

        /// <inheritdoc/>
        public override string ToString() => _endpoint.ToString();

        internal ColocEndpoint(Endpoint endpoint) => _endpoint = endpoint;
    }
}
