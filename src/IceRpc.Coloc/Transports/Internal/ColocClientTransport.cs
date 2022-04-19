// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Security;

namespace IceRpc.Transports.Internal
{
    /// <summary>Implements <see cref="IClientTransport{ISimpleNetworkConnection}"/> for the coloc transport.</summary>
    internal class ColocClientTransport : IClientTransport<ISimpleNetworkConnection>
    {
        /// <inheritdoc/>
        public string Name => ColocTransport.Name;

        private readonly ConcurrentDictionary<Endpoint, ColocListener> _listeners;

        /// <inheritdoc/>
        ISimpleNetworkConnection IClientTransport<ISimpleNetworkConnection>.CreateConnection(
            Endpoint remoteEndpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            ILogger logger)
        {
            if (authenticationOptions != null)
            {
                throw new NotSupportedException("cannot create a secure Coloc connection");
            }

            if (!ColocTransport.CheckEndpointParams(remoteEndpoint.Params))
            {
                throw new FormatException($"cannot create a Coloc connection to endpoint '{remoteEndpoint}'");
            }

            remoteEndpoint = remoteEndpoint.WithTransport(Name);

            if (_listeners.TryGetValue(remoteEndpoint, out ColocListener? listener))
            {
                (PipeReader reader, PipeWriter writer) = listener.NewClientConnection();
                return new ColocNetworkConnection(remoteEndpoint, isServer: false, writer, reader);
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
