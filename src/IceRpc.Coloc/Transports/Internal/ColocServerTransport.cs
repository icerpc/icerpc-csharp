// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Security;

namespace IceRpc.Transports.Internal
{
    /// <summary>Implements <see cref="IServerTransport{ISimpleNetworkConnection}"/> for the coloc transport.</summary>
    internal class ColocServerTransport : IServerTransport<ISimpleNetworkConnection>
    {
        /// <inheritdoc/>
        public string Name => ColocTransport.Name;

        private readonly ConcurrentDictionary<Endpoint, ColocListener> _listeners;

        /// <inheritdoc/>
        IListener<ISimpleNetworkConnection> IServerTransport<ISimpleNetworkConnection>.Listen(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            ILogger logger)
        {
            if (authenticationOptions != null)
            {
                throw new NotSupportedException("cannot create secure Coloc server");
            }

            var listener = new ColocListener(endpoint.WithTransport(Name));
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
