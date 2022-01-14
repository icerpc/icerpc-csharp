// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO.Pipelines;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{ISimpleNetworkConnection}"/> for the coloc transport.</summary>
    internal class ColocClientTransport : IClientTransport<ISimpleNetworkConnection>
    {
        private readonly ConcurrentDictionary<Endpoint, ColocListener> _listeners;

        /// <inheritdoc/>
        ISimpleNetworkConnection IClientTransport<ISimpleNetworkConnection>.CreateConnection(
            Endpoint remoteEndpoint,
            ILogger logger)
        {
            remoteEndpoint = remoteEndpoint.WithTransport(ColocTransport.Name);

            if (remoteEndpoint.Params.Count > 1)
            {
                throw new ArgumentException("unknown endpoint parameter", nameof(remoteEndpoint));
            }

            if (_listeners.TryGetValue(remoteEndpoint, out ColocListener? listener))
            {
                (PipeReader reader, PipeWriter writer) = listener.NewClientConnection();
                return new ColocNetworkConnection(remoteEndpoint, isServer: false, reader, writer);
            }
            else
            {
                throw new ConnectionRefusedException();
            }
        }

        internal ColocClientTransport(ConcurrentDictionary<Endpoint, ColocListener> listeners) =>
            _listeners = listeners;
    }
}
