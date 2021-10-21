// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>Gives Server the ability to create incoming transport connections.</summary>
    public interface IServerTransport<T> where T : INetworkConnection
    {
        /// <summary>Starts listening on an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>The new listener.</returns>
        /// <exception name="UnknownTransportException">Thrown if this server transport does not support the
        /// endpoint's transport.</exception>
        IListener<T> Listen(Endpoint endpoint);
    }
}
