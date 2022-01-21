// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport{ISimpleNetworkConnection}"/> for the coloc transport.</summary>
    internal class ColocServerTransport : IServerTransport<ISimpleNetworkConnection>
    {
        private readonly ConcurrentDictionary<Endpoint, ColocListener> _listeners;

        /// <inheritdoc/>
        IListener<ISimpleNetworkConnection> IServerTransport<ISimpleNetworkConnection>.Listen(
            Endpoint endpoint,
            ILogger logger)
        {
            var listener = new ColocListener(endpoint);
            if (!_listeners.TryAdd(listener.Endpoint, listener))
            {
                throw new TransportException($"endpoint '{listener.Endpoint}' is already in use");
            }
            return listener;
        }

        internal ColocServerTransport(ConcurrentDictionary<Endpoint, ColocListener> listeners) =>
            _listeners = listeners;
    }
}
